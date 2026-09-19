using System.Runtime.InteropServices;

namespace ErdtreeKeeper.Core;

/// <summary>Игра, установленная через Steam.</summary>
public sealed record SteamGame(string AppId, string Name, string InstallDir)
{
    /// <summary>Папки облачных сохранений этой игры - по одной на аккаунт Steam.</summary>
    public IReadOnlyList<string> CloudFolders { get; init; } = [];

    public bool HasCloudSaves => CloudFolders.Count > 0;
}

/// <summary>
/// Что известно о Steam на этом компьютере: где он стоит, какие игры
/// установлены и где лежат их облачные сохранения.
///
/// Нужно для одного - чтобы добавить игру можно было выбором из списка, а не
/// поиском папки вслепую. Steam хранит облачные сейвы единообразно:
/// userdata/&lt;аккаунт&gt;/&lt;appid&gt;/remote, и это готовый ответ на вопрос
/// "где у этой игры сохранения".
///
/// Читаются только текстовые файлы самого Steam. Ничего не запускается, ничего
/// не меняется, в сеть программа за этим не ходит.
/// </summary>
public static class SteamLibrary
{
    /// <summary>
    /// Папка Steam. Сначала спрашиваем сам Steam через реестр, и только если
    /// его там нет - смотрим обычные места установки.
    /// </summary>
    public static string? FindRoot()
    {
        var fromRegistry = ReadRegistryString("Software\\Valve\\Steam", "SteamPath");
        if (fromRegistry is not null && Directory.Exists(fromRegistry)) return fromRegistry;

        foreach (var candidate in LikelyRoots())
        {
            if (Directory.Exists(Path.Combine(candidate, "steamapps"))) return candidate;
        }

        return null;
    }

    private static IEnumerable<string> LikelyRoots()
    {
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (x86.Length > 0) yield return Path.Combine(x86, "Steam");

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (programFiles.Length > 0) yield return Path.Combine(programFiles, "Steam");

        // Вторая и третья библиотеки чаще всего лежат в корне другого диска.
        foreach (var drive in SafeDrives())
        {
            yield return Path.Combine(drive, "Steam");
            yield return Path.Combine(drive, "SteamLibrary");
        }
    }

    private static IEnumerable<string> SafeDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }

        foreach (var drive in drives)
        {
            var name = "";
            try { if (drive.IsReady) name = drive.RootDirectory.FullName; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            if (name.Length > 0) yield return name;
        }
    }

    /// <summary>
    /// Все папки библиотек: основная и те, что перечислены в
    /// libraryfolders.vdf. Игры нередко стоят на другом диске.
    /// </summary>
    public static IReadOnlyList<string> Libraries(string? steamRoot = null)
    {
        var root = steamRoot ?? FindRoot();
        if (root is null) return [];

        var found = new List<string> { root };

        var index = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        foreach (var path in ReadLibraryPaths(TryReadText(index)))
        {
            if (!found.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(path);
            }
        }

        return found;
    }

    /// <summary>Пути библиотек из содержимого libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> ReadLibraryPaths(string? vdf)
    {
        if (string.IsNullOrEmpty(vdf)) return [];

        return Vdf.Pairs(vdf)
            .Where(pair => pair.Key.Equals("path", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Служебные записи Steam, которые игрой не являются и сохранений не
    /// имеют. Без них в списке первыми оказываются "SteamVR" и наборы
    /// библиотек, а нужной игры не видно.
    /// </summary>
    private static readonly HashSet<string> NotGames =
    [
        "228980",  // Steamworks Common Redistributables
        "250820",  // SteamVR
        "1070560", // Steam Linux Runtime 1.0
        "1391110", // Steam Linux Runtime 2.0
        "1493710", // Proton Experimental
        "1628350", // Steam Linux Runtime 3.0
    ];

    /// <summary>
    /// Установленные игры со всех библиотек, вместе с папками облачных
    /// сохранений. Отсортированы по названию - список выбирают глазами.
    /// </summary>
    public static IReadOnlyList<SteamGame> InstalledGames(string? steamRoot = null)
    {
        var root = steamRoot ?? FindRoot();
        if (root is null) return [];

        var cloud = CloudFoldersByApp(root);
        var games = new Dictionary<string, SteamGame>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in Libraries(root))
        {
            var steamapps = Path.Combine(library, "steamapps");
            foreach (var manifest in SafeFiles(steamapps, "appmanifest_*.acf"))
            {
                var game = ReadManifest(TryReadText(manifest));
                if (game is null || games.ContainsKey(game.AppId)) continue;
                if (NotGames.Contains(game.AppId)) continue;

                games[game.AppId] = game with
                {
                    CloudFolders = cloud.TryGetValue(game.AppId, out var folders) ? folders : [],
                };
            }
        }

        return games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Разбирает appmanifest_*.acf: номер игры, название и папку установки.</summary>
    public static SteamGame? ReadManifest(string? acf)
    {
        if (string.IsNullOrEmpty(acf)) return null;

        string? appId = null, name = null, installDir = null;

        foreach (var (key, value) in Vdf.Pairs(acf))
        {
            if (appId is null && key.Equals("appid", StringComparison.OrdinalIgnoreCase)) appId = value;
            else if (name is null && key.Equals("name", StringComparison.OrdinalIgnoreCase)) name = value;
            else if (installDir is null && key.Equals("installdir", StringComparison.OrdinalIgnoreCase)) installDir = value;

            if (appId is not null && name is not null && installDir is not null) break;
        }

        if (string.IsNullOrWhiteSpace(appId)) return null;

        return new SteamGame(appId, string.IsNullOrWhiteSpace(name) ? appId : name, installDir ?? "");
    }

    /// <summary>
    /// Облачные папки по номеру игры: userdata/&lt;аккаунт&gt;/&lt;appid&gt;/remote.
    ///
    /// У одной игры их бывает несколько - по числу аккаунтов Steam на машине.
    /// </summary>
    public static Dictionary<string, List<string>> CloudFoldersByApp(string? steamRoot = null)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var root = steamRoot ?? FindRoot();
        if (root is null) return result;

        var userdata = Path.Combine(root, "userdata");
        if (!Directory.Exists(userdata)) return result;

        foreach (var account in SafeDirectories(userdata))
        {
            foreach (var app in SafeDirectories(account))
            {
                var remote = Path.Combine(app, "remote");
                if (!Directory.Exists(remote)) continue;

                // Пустая папка облака означает, что игру ни разу не запускали
                // на этом аккаунте: показывать её как источник сейвов незачем.
                if (!SafeFiles(remote, "*").Any() && !SafeDirectories(remote).Any()) continue;

                var appId = Path.GetFileName(app);
                if (!result.TryGetValue(appId, out var list)) result[appId] = list = [];
                list.Add(remote);
            }
        }

        return result;
    }

    private static string? TryReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static IEnumerable<string> SafeFiles(string folder, string pattern)
    {
        try { return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, pattern) : []; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try { return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder) : []; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    // ─── Реестр ─────────────────────────────────────────────────────────

    private const int HkeyCurrentUser = unchecked((int)0x80000001);
    private const uint RrfRtRegSz = 0x00000002;
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;

    /// <summary>
    /// Строка из реестра текущего пользователя.
    ///
    /// Через системный вызов, а не через библиотеку работы с реестром: та
    /// тянет за собой зависимость, которой в сборке нет, а нужна ровно одна
    /// строка - путь, который Steam сам о себе записал.
    /// </summary>
    private static string? ReadRegistryString(string subKey, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            uint size = 0;
            var status = RegGetValue(HkeyCurrentUser, subKey, valueName, RrfRtRegSz, out _, null, ref size);
            if (status != ErrorSuccess && status != ErrorMoreData) return null;
            if (size == 0 || size > 4096) return null;

            var buffer = new byte[size];
            status = RegGetValue(HkeyCurrentUser, subKey, valueName, RrfRtRegSz, out _, buffer, ref size);
            if (status != ErrorSuccess) return null;

            var text = System.Text.Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegGetValueW")]
    private static extern int RegGetValue(
        int hkey, string subKey, string value, uint flags, out uint type, byte[]? data, ref uint dataSize);
}

/// <summary>
/// Минимальный разбор формата Steam (VDF/ACF).
///
/// Формат - вложенные блоки из строк в кавычках. Нам нужны только пары
/// "ключ" "значение", и полноценный разбор дерева был бы лишней работой:
/// имена ключей, которые нас интересуют, в этих файлах не повторяются.
/// </summary>
public static class Vdf
{
    /// <summary>Все пары "ключ" "значение" в порядке появления.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Pairs(string text)
    {
        string? pending = null;

        foreach (var token in Tokens(text))
        {
            if (pending is null)
            {
                pending = token.Value;
                continue;
            }

            if (token.IsQuoted)
            {
                yield return new KeyValuePair<string, string>(pending, token.Value);
                pending = null;
            }
            else
            {
                // За ключом пошёл блок в скобках - значения у него нет.
                pending = null;
            }
        }
    }

    private readonly record struct Token(string Value, bool IsQuoted);

    private static IEnumerable<Token> Tokens(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];

            if (ch == '"')
            {
                var builder = new System.Text.StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    // Пути в этих файлах записаны с двойными обратными косыми.
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        builder.Append(text[i] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            var other => other,
                        });
                    }
                    else
                    {
                        builder.Append(text[i]);
                    }

                    i++;
                }

                i++;
                yield return new Token(builder.ToString(), true);
                continue;
            }

            if (ch is '{' or '}')
            {
                i++;
                yield return new Token(ch.ToString(), false);
                continue;
            }

            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            i++;
        }
    }
}
