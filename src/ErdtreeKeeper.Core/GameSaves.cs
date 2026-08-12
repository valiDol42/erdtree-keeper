namespace ErdtreeKeeper.Core;

/// <summary>Папка сохранений одного Steam-аккаунта.</summary>
public sealed record SaveAccount(string SteamId, string Path, DateTime Modified)
{
    /// <summary>Признак того, что Steam синхронизирует эту папку с облаком.</summary>
    public bool HasSteamCloudMarker => File.Exists(System.IO.Path.Combine(Path, "steam_autocloud.vdf"));
}

/// <summary>Файл сохранения внутри папки аккаунта.</summary>
public sealed record SaveFile(string Name, string Path, long Length, DateTime Modified)
{
    /// <summary>Сейв мода Seamless Co-op лежит рядом с обычным и имеет своё расширение.</summary>
    public bool IsSeamlessCoop => Name.EndsWith(".co2", StringComparison.OrdinalIgnoreCase);

    /// <summary>Резервная копия, которую делает сама игра.</summary>
    public bool IsGameBackup => Name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Поиск сохранений Elden Ring на диске.
///
/// Игра держит их в %APPDATA%\EldenRing\&lt;SteamID&gt;\, по папке на аккаунт.
/// Ничего, кроме перечисления файлов, здесь не происходит.
/// </summary>
public static class GameSaves
{
    /// <summary>Стандартная папка сохранений Elden Ring.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EldenRing");

    /// <summary>Расширения, которые считаем сохранениями.</summary>
    public static readonly string[] SaveExtensions = [".sl2", ".co2"];

    public static bool LooksLikeSave(string fileName) =>
        SaveExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        || fileName.EndsWith(".sl2.bak", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".co2.bak", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Перечисляет аккаунты, у которых есть хотя бы один файл сохранения.
    /// Свежие - первыми: почти всегда нужен именно тот, в который играли.
    /// </summary>
    public static List<SaveAccount> FindAccounts(string? root = null)
    {
        var dir = root ?? DefaultRoot;
        var accounts = new List<SaveAccount>();
        if (!Directory.Exists(dir)) return accounts;

        foreach (var sub in SafeEnumerateDirectories(dir))
        {
            var files = SafeEnumerateFiles(sub).Where(f => LooksLikeSave(Path.GetFileName(f))).ToList();
            if (files.Count == 0) continue;

            var main = Path.Combine(sub, "ER0000.sl2");
            var modified = File.Exists(main)
                ? File.GetLastWriteTime(main)
                : files.Max(File.GetLastWriteTime);

            accounts.Add(new SaveAccount(Path.GetFileName(sub), sub, modified));
        }

        return accounts.OrderByDescending(a => a.Modified).ToList();
    }

    /// <summary>Перечисляет файлы сохранений внутри папки аккаунта.</summary>
    public static List<SaveFile> FindSaveFiles(string accountPath)
    {
        var files = new List<SaveFile>();
        if (!Directory.Exists(accountPath)) return files;

        foreach (var path in SafeEnumerateFiles(accountPath))
        {
            var name = Path.GetFileName(path);
            if (!LooksLikeSave(name)) continue;

            try
            {
                var info = new FileInfo(path);
                files.Add(new SaveFile(name, path, info.Length, info.LastWriteTime));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Основной ER0000.sl2 наверх, резервные копии игры - вниз.
        return files
            .OrderBy(f => f.IsGameBackup)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Запущена ли игра. Восстанавливать сейв поверх работающей игры бесполезно.</summary>
    public static bool IsGameRunning()
    {
        string[] names = ["eldenring", "start_protected_game", "nightreign"];
        foreach (var name in names)
        {
            try
            {
                if (System.Diagnostics.Process.GetProcessesByName(name).Length > 0) return true;
            }
            catch (InvalidOperationException) { }
            catch (PlatformNotSupportedException) { }
        }

        return false;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try { return Directory.EnumerateFiles(path); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
