namespace ErdtreeKeeper.Core;

/// <summary>Папка сохранений одного Steam-аккаунта.</summary>
public sealed record SaveAccount(string SteamId, string Path, DateTime Modified)
{
    /// <summary>
    /// Профиль Windows, в котором нашлась папка, - пусто, если это свой.
    ///
    /// Два профиля дают две папки с одинаковым номером аккаунта Steam, и без
    /// имени профиля в списке их не различить.
    /// </summary>
    public string? WindowsProfile { get; init; }

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
/// Поиск сохранений на диске.
///
/// Где искать и что считать сохранением, задаёт профиль игры: Elden Ring держит
/// файлы в %APPDATA%\EldenRing\&lt;SteamID&gt;\, Dark Souls III - в
/// %APPDATA%\DarkSoulsIII\&lt;SteamID&gt;\, добавленная вручную игра - там, где
/// её папку указали. Ничего, кроме перечисления файлов, здесь не происходит.
/// </summary>
public static class GameSaves
{
    /// <summary>Стандартная папка сохранений Elden Ring.</summary>
    public static string DefaultRoot => GameProfiles.EldenRing.ResolveRoot();

    /// <summary>Расширения сохранений Elden Ring.</summary>
    public static readonly string[] SaveExtensions = [".sl2", ".co2"];

    public static bool LooksLikeSave(string fileName) =>
        GameProfiles.EldenRing.LooksLikeSave(fileName);

    /// <summary>
    /// Перечисляет аккаунты, у которых есть хотя бы один файл сохранения.
    /// Свежие - первыми: почти всегда нужен именно тот, в который играли.
    ///
    /// Обычно игра заводит по папке на аккаунт Steam, но так делают не все:
    /// часть игр кладёт сейвы прямо в свою папку. Тогда "аккаунтом" становится
    /// сама папка - иначе список оказался бы пустым при живых файлах внутри.
    /// </summary>
    public static List<SaveAccount> FindAccounts(GameProfile game, string? root = null)
    {
        // Заданная папка - только она: так работает инструмент снимков экрана.
        // Без неё обходим все известные места, включая чужие профили Windows.
        var roots = root is null ? game.ResolveRoots() : [root];

        var accounts = new List<SaveAccount>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in roots)
        {
            if (!SafeExists(dir) || !seen.Add(System.IO.Path.GetFullPath(dir))) continue;

            foreach (var sub in SafeEnumerateDirectories(dir))
            {
                var account = Describe(game, sub);
                if (account is not null) accounts.Add(account);
            }

            var atRoot = Describe(game, dir);
            if (atRoot is not null) accounts.Add(atRoot);
        }

        return accounts.OrderByDescending(a => a.Modified).ToList();
    }

    private static bool SafeExists(string path)
    {
        try { return Directory.Exists(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Аккаунты Elden Ring - прежняя подпись, которой пользуются тесты и стенд.</summary>
    public static List<SaveAccount> FindAccounts(string? root = null) =>
        FindAccounts(GameProfiles.EldenRing, root);

    /// <summary>Папка как аккаунт, если внутри есть сохранения. Иначе - ничего.</summary>
    private static SaveAccount? Describe(GameProfile game, string folder)
    {
        var files = SafeEnumerateFiles(folder)
            .Where(f => game.LooksLikeSave(System.IO.Path.GetFileName(f)))
            .ToList();
        if (files.Count == 0) return null;

        DateTime modified;
        try
        {
            var main = game.PrimaryFile is null ? null : System.IO.Path.Combine(folder, game.PrimaryFile);
            modified = main is not null && File.Exists(main)
                ? File.GetLastWriteTime(main)
                : files.Max(File.GetLastWriteTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return new SaveAccount(
            System.IO.Path.GetFileName(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar)),
            folder,
            modified)
        {
            WindowsProfile = WindowsProfiles.NameOf(folder),
        };
    }

    /// <summary>Перечисляет файлы сохранений внутри папки аккаунта.</summary>
    public static List<SaveFile> FindSaveFiles(GameProfile game, string accountPath)
    {
        var files = new List<SaveFile>();
        if (!Directory.Exists(accountPath)) return files;

        foreach (var path in SafeEnumerateFiles(accountPath))
        {
            var name = System.IO.Path.GetFileName(path);
            if (!game.LooksLikeSave(name)) continue;

            try
            {
                var info = new FileInfo(path);
                files.Add(new SaveFile(name, path, info.Length, info.LastWriteTime));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Главный файл наверх, резервные копии игры - вниз.
        return files
            .OrderBy(f => f.IsGameBackup)
            .ThenByDescending(f => game.PrimaryFile is not null
                                   && f.Name.Equals(game.PrimaryFile, StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<SaveFile> FindSaveFiles(string accountPath) =>
        FindSaveFiles(GameProfiles.EldenRing, accountPath);

    /// <summary>
    /// Лежит ли путь внутри папки сохранений игры.
    ///
    /// Снимки и автосохранения туда складывать нельзя: ротация начнёт удалять
    /// файлы игры, а снимок с именем игрового файла перезапишет живой сейв в
    /// обход восстановления - то есть в обход обязательной резервной копии.
    /// </summary>
    public static bool IsInsideGameFolder(string? path, string? root = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        // Без явного корня проверяем все известные игры разом: папка снимков
        // одна на игру, а запретить надо любую игровую.
        if (root is null)
        {
            foreach (var game in GameProfiles.BuiltIn)
            {
                foreach (var candidate in game.ResolveRoots())
                {
                    if (IsInside(path, candidate)) return true;
                }
            }

            return false;
        }

        return IsInside(path, root);
    }

    /// <summary>То же для конкретной игры, включая добавленные вручную.</summary>
    public static bool IsInsideGameFolder(GameProfile game, string? path, string? root = null) =>
        IsInside(path, root ?? game.ResolveRoot()) || IsInsideGameFolder(path);

    private static bool IsInside(string? path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;

        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var gameRoot = System.IO.Path.GetFullPath(root);

            return full.Equals(gameRoot, StringComparison.OrdinalIgnoreCase)
                   || full.StartsWith(
                       gameRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar)
                       + System.IO.Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }

    /// <summary>
    /// Запущена ли игра. Восстанавливать сейв поверх работающей игры бесполезно:
    /// она держит сохранение в памяти и перезапишет файл при выходе.
    ///
    /// Для игры, добавленной вручную, имя процесса может быть неизвестно -
    /// тогда ответ отрицательный, и предупреждение просто не показывается.
    /// Обещать больше, чем программа может проверить, здесь нельзя.
    /// </summary>
    public static bool IsGameRunning(GameProfile game)
    {
        foreach (var name in game.ProcessNames)
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

    public static bool IsGameRunning() => IsGameRunning(GameProfiles.EldenRing);

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
