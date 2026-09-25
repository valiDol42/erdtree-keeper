using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErdtreeKeeper.Core;

/// <summary>Файл, приложенный к выпуску.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>Выпуск программы на GitHub.</summary>
public sealed record ReleaseInfo(
    string Tag,
    string Version,
    string Notes,
    string PageUrl,
    IReadOnlyList<ReleaseAsset> Assets,
    DateTimeOffset? Published)
{
    /// <summary>Архив со сборкой для Windows.</summary>
    public ReleaseAsset? Package => Assets.FirstOrDefault(a =>
        a.Name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase));

    /// <summary>Файл с контрольными суммами - без него обновление не ставится.</summary>
    public ReleaseAsset? Checksums => Assets.FirstOrDefault(a =>
        a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
}

// Поля выпуска, которые нужны программе. Остальное в ответе GitHub есть, но
// читать лишнее незачем - а чего нет в этих классах, то и не разбирается.
internal sealed class ReleaseDto
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
}

internal sealed class AssetDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("browser_download_url")] public string? Url { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}

[JsonSerializable(typeof(ReleaseDto))]
internal partial class ReleaseJsonContext : JsonSerializerContext;

/// <summary>
/// Обновление программы: всё, что можно решить, не выходя в сеть.
///
/// Разбор ответа, сравнение версий, выбор файла, проверка адреса и
/// контрольных сумм живут здесь и покрыты тестами. Сам запрос делает
/// отдельный класс в приложении - так видно, где именно программа
/// обращается наружу, и таких мест ровно одно.
///
/// Правило, которое здесь важнее остального: адрес для скачивания берётся не
/// на веру. Ответ сервера - это данные, а не команда, и если в нём окажется
/// ссылка на посторонний узел, файл оттуда качаться не будет.
/// </summary>
public static class AppUpdate
{
    public const string Repository = "valiDol42/erdtree-keeper";

    /// <summary>Единственный адрес, по которому программа спрашивает о новой версии.</summary>
    public static string LatestReleaseUrl => $"https://api.github.com/repos/{Repository}/releases/latest";

    /// <summary>Страница выпусков - её открывает браузер, а не программа.</summary>
    public static string ReleasesPageUrl => $"https://github.com/{Repository}/releases";

    /// <summary>
    /// Узлы, с которых программе позволено качать.
    ///
    /// GitHub отдаёт файлы выпусков с отдельных доменов, и список составлен по
    /// ним. Всё, что не отсюда, отвергается, даже если так ответил сам GitHub:
    /// подменённый или взломанный ответ иначе привёл бы к скачиванию чужого
    /// файла.
    /// </summary>
    public static readonly string[] TrustedHosts =
    [
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    ];

    /// <summary>Можно ли обращаться по этому адресу.</summary>
    public static bool IsTrustedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        // Только https: по http ответ можно подменить по дороге.
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        return TrustedHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Разбирает ответ GitHub о последнем выпуске.
    ///
    /// Черновики и предварительные выпуски отбрасываются: их публикуют, чтобы
    /// посмотреть самому, а не чтобы их ставили игроки.
    /// </summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        ReleaseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, ReleaseJsonContext.Default.ReleaseDto);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto is null || dto.Draft || dto.Prerelease) return null;

        var tag = dto.TagName?.Trim();
        if (string.IsNullOrEmpty(tag)) return null;

        var assets = (dto.Assets ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && IsTrustedUrl(a.Url))
            .Select(a => new ReleaseAsset(a.Name!, a.Url!, a.Size))
            .ToList();

        return new ReleaseInfo(
            tag,
            NormalizeVersion(tag),
            ReadableNotes(dto.Body),
            IsTrustedUrl(dto.HtmlUrl) ? dto.HtmlUrl! : ReleasesPageUrl,
            assets,
            dto.PublishedAt);
    }

    /// <summary>
    /// Граница в тексте выпуска: выше - что изменилось, ниже - как проверить
    /// и установить файл. GitHub комментарий не показывает, а программа по нему
    /// отрезает то, что человеку в окне обновления не нужно.
    /// </summary>
    public const string NotesEndMarker = "<!-- keeper:end-of-changes -->";

    /// <summary>
    /// Текст выпуска так, как его стоит показать в окне программы.
    ///
    /// На GitHub он написан в Markdown, а окно показывает простой текст, и
    /// без обработки человек видел бы решётки, звёздочки и обратные кавычки.
    /// Разметка снимается, смысл остаётся: заголовок становится строкой,
    /// пункт списка - строкой с точкой.
    /// </summary>
    public static string ReadableNotes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";

        var text = body.Replace("\r\n", "\n");
        var cut = text.IndexOf(NotesEndMarker, StringComparison.Ordinal);
        if (cut >= 0) text = text[..cut];

        var lines = new List<string>();
        var headings = new HashSet<int>();
        var inCode = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();

            // Блоки кода в заметках - команды проверки; в окне они не нужны.
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }

            if (inCode) continue;

            var trimmed = line.TrimStart();
            var startsBlock = false;

            if (trimmed.StartsWith('#'))
            {
                line = trimmed.TrimStart('#').Trim();
                startsBlock = true;
            }
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                line = "·  " + trimmed[2..];
                startsBlock = true;
            }
            else
            {
                line = trimmed;
            }

            line = line.Replace("**", "").Replace("`", "");

            // Пустые строки подряд схлопываются в одну.
            if (line.Length == 0)
            {
                if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
                continue;
            }

            // Журнал изменений перенесён вручную по ширине редактора. В окне
            // своя ширина, и такие переносы давали бы рваные строки, поэтому
            // продолжение абзаца или пункта приклеивается к началу.
            if (!startsBlock && lines.Count > 0 && lines[^1].Length > 0 && !headings.Contains(lines.Count - 1))
            {
                lines[^1] += " " + line;
                continue;
            }

            lines.Add(line);
            if (trimmed.StartsWith('#')) headings.Add(lines.Count - 1);
        }

        return string.Join('\n', lines).Trim();
    }

    /// <summary>Версия из тега: "v1.4.1" и "1.4.1" - одно и то же.</summary>
    public static string NormalizeVersion(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        return text;
    }

    /// <summary>
    /// Сравнение версий по числам, а не по алфавиту: иначе 1.10 оказалась бы
    /// старше 1.9. Отрицательное значение - первая меньше.
    /// </summary>
    public static int CompareVersions(string left, string right)
    {
        var a = Parts(left);
        var b = Parts(right);

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }

        return 0;
    }

    private static int[] Parts(string version)
    {
        var text = NormalizeVersion(version);

        // Хвост вида "-rc1" или "+сборка" на порядок версий не влияет: до
        // сравнения таких выпусков дело не доходит, они отсеиваются раньше.
        var cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0) text = text[..cut];

        return text
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var number) ? number : 0)
            .ToArray();
    }

    /// <summary>Новее ли выпуск того, что установлено.</summary>
    public static bool IsNewer(string candidate, string current) =>
        CompareVersions(candidate, current) > 0;

    /// <summary>
    /// Разбирает SHA256SUMS.txt: строки вида "сумма  имя файла".
    ///
    /// Это и есть та проверка, ради которой файл вообще качается отдельно:
    /// скачанный архив сверяется с суммой, опубликованной вместе с выпуском.
    /// </summary>
    public static Dictionary<string, string> ParseChecksums(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var space = line.IndexOf(' ');
            if (space <= 0) continue;

            var hash = line[..space].Trim();
            var name = line[space..].Trim().TrimStart('*').Trim();

            if (hash.Length != 64 || name.Length == 0) continue;
            if (!hash.All(Uri.IsHexDigit)) continue;

            result[name] = hash.ToLowerInvariant();
        }

        return result;
    }
}
