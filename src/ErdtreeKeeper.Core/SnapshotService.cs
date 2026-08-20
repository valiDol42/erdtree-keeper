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
        ? $"{Length / 1024.0 / 1024.0:0.0} {Loc.Get("unit.mb")}"
        : $"{Length / 1024.0:0} {Loc.Get("unit.kb")}";
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
    public static string RestoreBackupFolder => Loc.Get("path.restoreBackup");

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
            return new FileOperationResult(false, Loc.Get("op.sourceMissing"));

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        if (fileName.IndexOfAny(invalid) >= 0)
            return new FileOperationResult(false, Loc.Get("op.badName"));

        var destination = System.IO.Path.Combine(targetFolder, fileName);
        if (File.Exists(destination) && !overwrite)
            return new FileOperationResult(false, Loc.Get("op.nameTaken"), destination);

        try
        {
            Directory.CreateDirectory(targetFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileOperationResult(false, Loc.Get("err.createFolder", ex.Message));
        }

        try
        {
            _log.Read(Loc.Get("log.readingSave"), sourcePath);
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
                _log.Error(Loc.Get("log.copyMismatch"), temp);
                return new FileOperationResult(false, Loc.Get("op.copyMismatch"));
            }

            // Если игра дописывала сейв прямо во время копирования, снимок
            // получился бы обрывком. Перечитываем источник и сравниваем.
            var again = await Sl2File.ReadAllBytesSharedAsync(sourcePath, ct).ConfigureAwait(false);
            var recheck = Convert.ToHexStringLower(SHA256.HashData(again));
            if (recheck != sourceHash)
            {
                TryDelete(temp);
                _log.Warn(Loc.Get("log.gameWriting"), sourcePath);
                return new FileOperationResult(false,
                    Loc.Get("op.gameWriting"));
            }

            File.Move(temp, destination, overwrite: true);

            var integrity = Sl2File.CheckIntegrity(bytes);
            _log.Write(Loc.Get("log.snapshotCreated", bytes.Length / 1024 / 1024), destination);

            var message = integrity.FileRecognised && !integrity.AllOk
                ? Loc.Get("op.createdDamaged", integrity.BadCount)
                : Loc.Get("op.created");

            return new FileOperationResult(true, message, destination, sourceHash, integrity);
        }
        catch (OperationCanceledException)
        {
            return new FileOperationResult(false, Loc.Get("status.cancelled"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error(Loc.Get("err.createSnapshot", ex.Message), destination);
            return new FileOperationResult(false, Loc.Get("err.createSnapshot", ex.Message));
        }
    }

    /// <summary>
    /// Возвращает снимок в игру.
    ///
    /// Порядок жёсткий и не настраивается:
    /// 1. снимок проверяется целиком - негодный дальше не идёт;
    /// 2. текущий сейв уезжает в резервную копию, и она сверяется по SHA-256;
    /// 3. новый файл пишется рядом под временным именем, сверяется и только
    ///    потом занимает место игрового.
    ///
    /// Третий шаг важен: прямая запись 29 МБ поверх игрового файла сначала
    /// обнуляет его, и обрыв на середине оставил бы обрубок вместо сохранения.
    /// </summary>
    public async Task<FileOperationResult> RestoreAsync(
        string snapshotPath,
        string gameSavePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(snapshotPath))
            return new FileOperationResult(false, Loc.Get("op.snapshotMissing"));

        try
        {
            var bytes = await Sl2File.ReadAllBytesSharedAsync(snapshotPath, ct).ConfigureAwait(false);
            var integrity = Sl2File.CheckIntegrity(bytes);

            // Любая неисправность - отказ. Раньше проверка срабатывала только
            // для распознанных файлов, и пустой или обрезанный снимок проходил
            // насквозь, затирая игровой сейв.
            if (!integrity.AllOk)
            {
                _log.Error(Loc.Get("log.badSnapshot", integrity.Problem ?? ""), snapshotPath);
                return new FileOperationResult(false,
                    Loc.Get("op.badSnapshot", integrity.Problem ?? ""),
                    snapshotPath, null, integrity);
            }

            var sourceHash = Convert.ToHexStringLower(SHA256.HashData(bytes));

            string backupPath = Loc.Get("op.noPreviousFile");
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

                // Резервная копия, которая не совпала с оригиналом, бесполезна -
                // и выяснится это ровно тогда, когда она понадобится.
                var backupWritten = await File.ReadAllBytesAsync(backupPath, ct).ConfigureAwait(false);
                if (!SHA256.HashData(backupWritten).SequenceEqual(SHA256.HashData(current)))
                {
                    TryDelete(backupPath);
                    _log.Error(Loc.Get("log.backupMismatch"), backupPath);
                    return new FileOperationResult(false,
                        Loc.Get("op.backupFailed"));
                }

                _log.Write(Loc.Get("log.backupDone"), backupPath);
            }

            // Пишем рядом и подменяем одним движением: игровой файл до самого
            // конца остаётся прежним.
            var staging = gameSavePath + ".new";
            try
            {
                await File.WriteAllBytesAsync(staging, bytes, ct).ConfigureAwait(false);

                var staged = await File.ReadAllBytesAsync(staging, ct).ConfigureAwait(false);
                if (Convert.ToHexStringLower(SHA256.HashData(staged)) != sourceHash)
                {
                    TryDelete(staging);
                    _log.Error(Loc.Get("log.stagingMismatch"), staging);
                    return new FileOperationResult(false,
                        Loc.Get("op.writeFailed", System.IO.Path.GetFileName(backupPath)));
                }

                File.Move(staging, gameSavePath, overwrite: true);
            }
            catch
            {
                TryDelete(staging);
                throw;
            }

            _log.Write(Loc.Get("log.restored"), gameSavePath);
            return new FileOperationResult(
                true,
                Loc.Get("op.restored", System.IO.Path.GetFileName(backupPath)),
                gameSavePath,
                sourceHash,
                integrity);
        }
        catch (OperationCanceledException)
        {
            return new FileOperationResult(false, Loc.Get("status.cancelled"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error(Loc.Get("err.restore", ex.Message), gameSavePath);
            return new FileOperationResult(false, Loc.Get("err.restore", ex.Message));
        }
    }


    /// <summary>
    /// Удаляет старые автосохранения сверх лимита.
    ///
    /// Под удаление попадают ТОЛЬКО файлы с меткой времени в имени, то есть
    /// сделанные этой программой автоматически. Раньше удалялось всё, что
    /// похоже на сейв: если папку автосохранений навести на папку снимков или
    /// на папку игры, ротация сносила ручные копии и живой ER0000.sl2.
    /// </summary>
    public int Rotate(string autoFolder, int keep)
    {
        if (keep < 1) return 0;

        var ours = List(autoFolder)
            .Where(s => SnapshotNaming.IsAutoName(s.Name))
            .ToList();
        if (ours.Count <= keep) return 0;

        var removed = 0;
        foreach (var snapshot in ours.Skip(keep))
        {
            if (!TryDelete(snapshot.Path)) continue;
            removed++;
            _log.Deleted(Loc.Get("log.rotated"), snapshot.Path);
        }

        return removed;
    }

    public FileOperationResult Rename(string path, string newName)
    {
        if (newName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            return new FileOperationResult(false, Loc.Get("op.badName"));

        var folder = System.IO.Path.GetDirectoryName(path)!;
        var destination = System.IO.Path.Combine(folder, newName);
        if (File.Exists(destination))
            return new FileOperationResult(false, Loc.Get("op.nameTaken"));

        try
        {
            File.Move(path, destination);
            _log.Write(Loc.Get("op.renamed", newName), destination);
            return new FileOperationResult(true, Loc.Get("op.renamed", newName), destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileOperationResult(false, Loc.Get("err.rename", ex.Message));
        }
    }

    public FileOperationResult Delete(string path)
    {
        try
        {
            File.Delete(path);
            _log.Deleted(Loc.Get("op.deleted"), path);
            return new FileOperationResult(true, Loc.Get("op.deleted"), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileOperationResult(false, Loc.Get("err.delete", ex.Message));
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
