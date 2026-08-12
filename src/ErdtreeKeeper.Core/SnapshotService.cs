using System.Security.Cryptography;

namespace ErdtreeKeeper.Core;

/// <summary>Сохранённый снимок в папке пользователя.</summary>
public sealed record Snapshot(string Name, string Path, long Length, DateTime Created)
{
    /// <summary>
    /// Обычный сейв весит около 29 МБ, но в папке может оказаться и обрывок -
    /// его размер должен читаться, а не превращаться в "0,0 МБ".
    /// </summary>
    public string SizeText => Length >= 1024 * 1024
        ? $"{Length / 1024.0 / 1024.0:0.0} МБ"
        : $"{Length / 1024.0:0} КБ";
}

/// <summary>Чем закончилась операция с файлом.</summary>
public sealed record FileOperationResult(
    bool Success,
    string Message,
    string? Path = null,
    string? Sha256 = null,
    Sl2File.IntegrityReport? Integrity = null);

/// <summary>
/// Копирование сохранений и возврат их в игру.
///
/// Две гарантии, на которых всё держится:
///
/// 1. Копия сверяется по SHA-256, а не по размеру. Игра пишет сейв не мгновенно,
///    и файл, пойманный в момент записи, совпадёт по длине, но окажется
///    бессмысленным. Дополнительно пересчитываются все 11 контрольных сумм
///    самого сейва - так видно, что снимок игра вообще сможет загрузить.
///
/// 2. Перед возвратом снимка в игру текущий сейв всегда уезжает в резервную
///    копию. Перезапись игрового файла иначе необратима.
/// </summary>
public sealed class SnapshotService(ActivityLog log)
{
    /// <summary>Подпапка с копиями, которые программа делает перед восстановлением.</summary>
    public const string RestoreBackupFolder = "Перед восстановлением";

    /// <summary>
    /// Подпапка для автосохранений по умолчанию. Латиницей: путь может уехать
    /// в консоль, в архив или на диск с другой кодировкой, и кириллица там
    /// доставляет больше хлопот, чем пользы. Свою папку можно выбрать в
    /// настройках автосохранения.
    /// </summary>
    public const string AutoFolder = "autosave";

    private readonly ActivityLog _log = log;

    /// <summary>Перечисляет снимки в папке.</summary>
    public List<Snapshot> List(string folder)
    {
        var snapshots = new List<Snapshot>();
        if (!Directory.Exists(folder)) return snapshots;

        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                var name = System.IO.Path.GetFileName(path);
                if (!GameSaves.LooksLikeSave(name)) continue;

                var info = new FileInfo(path);
                snapshots.Add(new Snapshot(name, path, info.Length, info.LastWriteTime));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return snapshots.OrderByDescending(s => s.Created).ToList();
    }

    /// <summary>
    /// Делает снимок сохранения.
    ///
    /// Источник читается целиком, копия пишется во временный файл, затем обе
    /// стороны сверяются по SHA-256, и только после этого файл занимает своё
    /// имя. Незавершённая копия никогда не остаётся под настоящим именем.
    /// </summary>
    public async Task<FileOperationResult> CreateAsync(
        string sourcePath,
        string targetFolder,
        string fileName,
        bool overwrite,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            return new FileOperationResult(false, "Файл сохранения не найден");

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        if (fileName.IndexOfAny(invalid) >= 0)
            return new FileOperationResult(false, "В имени файла есть недопустимые символы");

        var destination = System.IO.Path.Combine(targetFolder, fileName);
        if (File.Exists(destination) && !overwrite)
            return new FileOperationResult(false, "Файл с таким именем уже есть", destination);

        try
        {
            Directory.CreateDirectory(targetFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileOperationResult(false, $"Не удалось создать папку: {ex.Message}");
        }

        try
        {
            _log.Read("Читаю сохранение", sourcePath);
            var bytes = await Sl2File.ReadAllBytesSharedAsync(sourcePath, ct).ConfigureAwait(false);
            var sourceHash = Convert.ToHexStringLower(SHA256.HashData(bytes));

            var temp = destination + ".part";
            await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);

            // Сверяем то, что реально легло на диск.
            var written = await File.ReadAllBytesAsync(temp, ct).ConfigureAwait(false);
            var writtenHash = Convert.ToHexStringLower(SHA256.HashData(written));
            if (writtenHash != sourceHash)
            {
                TryDelete(temp);
                _log.Error("Копия не совпала с оригиналом, файл удалён", temp);
                return new FileOperationResult(false, "Копия не совпала с оригиналом - попробуйте ещё раз");
            }

            // Если игра дописывала сейв прямо во время копирования, снимок
            // получился бы обрывком. Перечитываем источник и сравниваем.
            var again = await Sl2File.ReadAllBytesSharedAsync(sourcePath, ct).ConfigureAwait(false);
            var recheck = Convert.ToHexStringLower(SHA256.HashData(again));
            if (recheck != sourceHash)
            {
                TryDelete(temp);
                _log.Warn("Игра записывала сейв во время копирования, снимок отменён", sourcePath);
                return new FileOperationResult(false,
                    "Игра записывала сохранение прямо сейчас - подождите пару секунд и повторите");
            }

            File.Move(temp, destination, overwrite: true);

            var integrity = Sl2File.CheckIntegrity(bytes);
            _log.Write($"Снимок создан ({bytes.Length / 1024 / 1024} МБ)", destination);

            var message = integrity.FileRecognised && !integrity.AllOk
                ? $"Снимок создан, но в исходном сейве повреждённых блоков: {integrity.BadCount}"
                : "Снимок создан и проверен";

            return new FileOperationResult(true, message, destination, sourceHash, integrity);
        }
        catch (OperationCanceledException)
        {
            return new FileOperationResult(false, "Отменено");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Не удалось создать снимок: {ex.Message}", destination);
            return new FileOperationResult(false, $"Не удалось создать снимок: {ex.Message}");
        }
    }

    /// <summary>
    /// Возвращает снимок в игру.
    ///
    /// Текущий игровой файл сначала уезжает в резервную копию - всегда, без
    /// вопросов и без возможности отключить. Это единственное место, где
    /// программа пишет в папку игры.
    /// </summary>
    public async Task<FileOperationResult> RestoreAsync(
        string snapshotPath,
        string gameSavePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(snapshotPath))
            return new FileOperationResult(false, "Снимок не найден");

        try
        {
            var bytes = await Sl2File.ReadAllBytesSharedAsync(snapshotPath, ct).ConfigureAwait(false);
            var integrity = Sl2File.CheckIntegrity(bytes);

            // Возвращать заведомо битый файл в игру бессмысленно: она его не
            // примет и напишет "Save data is corrupt".
            if (integrity.FileRecognised && !integrity.AllOk)
            {
                _log.Error($"Снимок повреждён, блоков с ошибкой: {integrity.BadCount}", snapshotPath);
                return new FileOperationResult(false,
                    $"Снимок повреждён (блоков с ошибкой: {integrity.BadCount}) - игра его не загрузит",
                    snapshotPath, null, integrity);
            }

            string backupPath = "(файла не было)";
            if (File.Exists(gameSavePath))
            {
                var gameFolder = System.IO.Path.GetDirectoryName(gameSavePath)!;
                var backupFolder = System.IO.Path.Combine(gameFolder, RestoreBackupFolder);
                Directory.CreateDirectory(backupFolder);

                var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var baseName = System.IO.Path.GetFileNameWithoutExtension(gameSavePath);
                var extension = System.IO.Path.GetExtension(gameSavePath);
                backupPath = System.IO.Path.Combine(backupFolder, $"{baseName}_{stamp}{extension}");

                var current = await Sl2File.ReadAllBytesSharedAsync(gameSavePath, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(backupPath, current, ct).ConfigureAwait(false);
                _log.Write("Текущий сейв сохранён в резервную копию", backupPath);
            }

            var sourceHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await File.WriteAllBytesAsync(gameSavePath, bytes, ct).ConfigureAwait(false);

            var written = await Sl2File.ReadAllBytesSharedAsync(gameSavePath, ct).ConfigureAwait(false);
            if (Convert.ToHexStringLower(SHA256.HashData(written)) != sourceHash)
            {
                _log.Error("Записанный файл не совпал со снимком", gameSavePath);
                return new FileOperationResult(false,
                    "Записанный файл не совпал со снимком - восстановите резервную копию");
            }

            _log.Write("Снимок восстановлен в игру", gameSavePath);
            return new FileOperationResult(
                true,
                $"Восстановлено. Прежний сейв: {System.IO.Path.GetFileName(backupPath)}",
                gameSavePath,
                sourceHash,
                integrity);
        }
        catch (OperationCanceledException)
        {
            return new FileOperationResult(false, "Отменено");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Не удалось восстановить: {ex.Message}", gameSavePath);
            return new FileOperationResult(false, $"Не удалось восстановить: {ex.Message}");
        }
    }

    /// <summary>Удаляет старые автоснимки сверх лимита. Ручные снимки не трогает.</summary>
    public int Rotate(string autoFolder, int keep)
    {
        var snapshots = List(autoFolder);
        if (snapshots.Count <= keep) return 0;

        var removed = 0;
        foreach (var snapshot in snapshots.Skip(keep))
        {
            if (!TryDelete(snapshot.Path)) continue;
            removed++;
            _log.Deleted("Старый автоснимок удалён", snapshot.Path);
        }

        return removed;
    }

    public FileOperationResult Rename(string path, string newName)
    {
        if (newName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            return new FileOperationResult(false, "В имени файла есть недопустимые символы");

        var folder = System.IO.Path.GetDirectoryName(path)!;
        var destination = System.IO.Path.Combine(folder, newName);
        if (File.Exists(destination))
            return new FileOperationResult(false, "Файл с таким именем уже есть");

        try
        {
            File.Move(path, destination);
            _log.Write($"Переименовано в {newName}", destination);
            return new FileOperationResult(true, $"Переименовано в {newName}", destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileOperationResult(false, $"Не удалось переименовать: {ex.Message}");
        }
    }

    public FileOperationResult Delete(string path)
    {
        try
        {
            File.Delete(path);
            _log.Deleted("Снимок удалён", path);
            return new FileOperationResult(true, "Снимок удалён", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileOperationResult(false, $"Не удалось удалить: {ex.Message}");
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
