using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using ErdtreeKeeper.Core;

namespace ErdtreeKeeper.Updates;

/// <summary>Чем закончилась проверка.</summary>
public enum UpdateState
{
    /// <summary>Ещё не проверяли.</summary>
    Unknown,

    /// <summary>Разрешения на выход в сеть нет - и не будет, пока его не дадут.</summary>
    NotAllowed,

    /// <summary>Установлена последняя версия.</summary>
    UpToDate,

    /// <summary>Есть версия новее.</summary>
    Available,

    /// <summary>Проверить не вышло: нет сети, GitHub не ответил, ответ непонятен.</summary>
    Failed,
}

public sealed record UpdateStatus(UpdateState State, ReleaseInfo? Release, string Message);

/// <summary>
/// Единственное место в программе, которое обращается в интернет.
///
/// Почему это вообще появилось. Программа раздаётся одним файлом без
/// установщика, и до сих пор узнать о новой версии можно было, только зайдя на
/// GitHub. Это значит, что исправления доходили не до всех.
///
/// Что сделано, чтобы возможность выходить в сеть не превратилась в то, чего
/// игроки справедливо боятся:
///
/// 1. Без разрешения не уходит ни один запрос. Разрешение спрашивают один раз,
///    в отдельном окне, и до ответа сеть не трогается вовсе.
/// 2. Адрес один и записан в коде: выпуски этого репозитория на GitHub.
///    Ссылки из ответа проверяются по списку узлов - подменённый ответ не
///    заставит программу качать с постороннего сервера.
/// 3. Ничего не отправляется. В запросе нет ни идентификаторов, ни данных о
///    машине - только обращение за списком выпусков.
/// 4. Каждый запрос попадает в журнал действий, который виден в окне и
///    выгружается в файл.
/// 5. Скачанный архив сверяется с контрольной суммой из того же выпуска. Не
///    сошлось - файл удаляется и ничего не ставится.
/// </summary>
public sealed class UpdateService(ActivityLog log)
{
    /// <summary>Больше этого архив со сборкой не бывает - защита от бесконечного ответа.</summary>
    private const long MaxDownloadBytes = 300L * 1024 * 1024;

    private readonly ActivityLog _log = log;
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            // Перенаправления разрешены: GitHub отдаёт файлы выпусков с
            // отдельного узла. Куда именно нас увело, проверяется отдельно.
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        // GitHub отказывается отвечать запросам без представления.
        client.DefaultRequestHeaders.Add("User-Agent", $"ErdtreeKeeper/{AppInfo.Version}");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        return client;
    }

    /// <summary>Спрашивает GitHub о последнем выпуске и сравнивает с установленной версией.</summary>
    public async Task<UpdateStatus> CheckAsync(CancellationToken ct = default)
    {
        var url = AppUpdate.LatestReleaseUrl;
        _log.Network(Loc.Get("log.netCheck"), url);

        string json;
        try
        {
            json = await Client.GetStringAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _log.Error(Loc.Get("log.netFailed", ex.Message), url);
            return new UpdateStatus(UpdateState.Failed, null, Loc.Get("upd.failed", Short(ex)));
        }

        var release = AppUpdate.ParseRelease(json);
        if (release is null)
        {
            _log.Warn(Loc.Get("log.netUnreadable"), url);
            return new UpdateStatus(UpdateState.Failed, null, Loc.Get("upd.unreadable"));
        }

        var newer = AppUpdate.IsNewer(release.Version, AppInfo.Version);
        _log.Network(Loc.Get("log.netAnswer", release.Tag, newer ? Loc.Get("log.netNewer") : Loc.Get("log.netSame")), url);

        return newer
            ? new UpdateStatus(UpdateState.Available, release, Loc.Get("upd.available", release.Version))
            : new UpdateStatus(UpdateState.UpToDate, release, Loc.Get("upd.upToDate", AppInfo.Version));
    }

    /// <summary>Что получилось из скачивания.</summary>
    public sealed record Download(bool Success, string Message, string? Folder = null, string? Sha256 = null);

    /// <summary>
    /// Качает архив выпуска, сверяет его с опубликованной суммой и
    /// распаковывает во временную папку.
    ///
    /// Порядок именно такой: пока сумма не сошлась, распаковка не начинается.
    /// Архив из неизвестно чего не должен даже разворачиваться на диск.
    /// </summary>
    public async Task<Download> DownloadAsync(
        ReleaseInfo release,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (release.Package is not { } package)
            return new Download(false, Loc.Get("upd.noPackage"));

        if (release.Checksums is not { } sums)
            return new Download(false, Loc.Get("upd.noChecksums"));

        if (!AppUpdate.IsTrustedUrl(package.Url) || !AppUpdate.IsTrustedUrl(sums.Url))
            return new Download(false, Loc.Get("upd.badUrl"));

        var work = Path.Combine(Path.GetTempPath(), $"ErdtreeKeeper-update-{release.Version}");

        try
        {
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            Directory.CreateDirectory(work);

            _log.Network(Loc.Get("log.netSums"), sums.Url);
            var sumsText = await Client.GetStringAsync(sums.Url, ct).ConfigureAwait(false);
            var expected = AppUpdate.ParseChecksums(sumsText);

            if (!expected.TryGetValue(package.Name, out var wantedHash))
                return new Download(false, Loc.Get("upd.noSumForFile", package.Name));

            var archive = Path.Combine(work, package.Name);
            _log.Network(Loc.Get("log.netDownload", package.Size / 1024 / 1024), package.Url);
            await DownloadFileAsync(package.Url, archive, package.Size, progress, ct).ConfigureAwait(false);

            var actual = await HashAsync(archive, ct).ConfigureAwait(false);
            if (!actual.Equals(wantedHash, StringComparison.OrdinalIgnoreCase))
            {
                _log.Error(Loc.Get("log.updSumMismatch"), archive);
                TryDeleteFolder(work);
                return new Download(false, Loc.Get("upd.sumMismatch"));
            }

            _log.Info(Loc.Get("log.updSumOk", actual[..16]), archive);

            var unpacked = Path.Combine(work, "unpacked");
            Extract(archive, unpacked);

            // Внутри архива всё лежит в папке с именем выпуска, а не россыпью.
            // Ставить надо её содержимое, поэтому корнем считается папка, где
            // действительно оказался exe, а не верх распакованного дерева.
            var executable = FindExecutable(unpacked);
            if (executable is null)
            {
                TryDeleteFolder(work);
                return new Download(false, Loc.Get("upd.noExe", UpdateInstaller.ExecutableName));
            }

            var staging = Path.GetDirectoryName(executable)!;

            if (expected.TryGetValue(UpdateInstaller.ExecutableName, out var wantedExe))
            {
                var actualExe = await HashAsync(executable, ct).ConfigureAwait(false);
                if (!actualExe.Equals(wantedExe, StringComparison.OrdinalIgnoreCase))
                {
                    _log.Error(Loc.Get("log.updSumMismatch"), executable);
                    TryDeleteFolder(work);
                    return new Download(false, Loc.Get("upd.sumMismatch"));
                }
            }

            _log.Write(Loc.Get("log.updUnpacked"), staging);
            return new Download(true, Loc.Get("upd.ready", release.Version), staging, actual);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFolder(work);
            return new Download(false, Loc.Get("status.cancelled"));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       or InvalidDataException)
        {
            _log.Error(Loc.Get("log.updFailed", ex.Message), work);
            TryDeleteFolder(work);
            return new Download(false, Loc.Get("upd.downloadFailed", Short(ex)));
        }
    }

    private static async Task DownloadFileAsync(
        string url, string destination, long declaredSize, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await Client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Куда бы ни привели перенаправления, конечный узел тоже должен быть
        // из списка: доверие к первому адресу не распространяется на цепочку.
        var finalUrl = response.RequestMessage?.RequestUri?.ToString();
        if (finalUrl is not null && !AppUpdate.IsTrustedUrl(finalUrl))
            throw new HttpRequestException(Loc.Get("upd.badUrl"));

        var total = response.Content.Headers.ContentLength ?? declaredSize;
        if (total > MaxDownloadBytes) throw new InvalidDataException(Loc.Get("upd.tooBig"));

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(destination);

        var buffer = new byte[1 << 16];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            written += read;
            if (written > MaxDownloadBytes) throw new InvalidDataException(Loc.Get("upd.tooBig"));

            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            if (total > 0) progress?.Report(Math.Min(1.0, (double)written / total));
        }
    }

    /// <summary>
    /// Распаковка с проверкой каждого пути.
    ///
    /// Имя внутри архива - тоже данные из сети. Запись вида "..\..\windows\
    /// system32\..." увела бы файл за пределы папки, поэтому каждый путь
    /// сверяется с целевой папкой, а не просто склеивается с ней.
    /// </summary>
    private static void Extract(string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;

            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Loc.Get("upd.badEntry", entry.FullName));

            var folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>
    /// Ищет программу в распакованном дереве. Глубина ограничена: архив со
    /// сборкой плоский, и рыться в нём дальше первых уровней незачем.
    /// </summary>
    private static string? FindExecutable(string root)
    {
        var direct = Path.Combine(root, UpdateInstaller.ExecutableName);
        if (File.Exists(direct)) return direct;

        try
        {
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                var candidate = Path.Combine(folder, UpdateInstaller.ExecutableName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return null;
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void TryDeleteFolder(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Short(Exception ex) =>
        ex is TaskCanceledException ? Loc.Get("upd.timeout") : ex.Message;
}
