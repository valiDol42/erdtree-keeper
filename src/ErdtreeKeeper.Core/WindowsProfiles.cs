namespace ErdtreeKeeper.Core;

/// <summary>
/// Профили пользователей Windows на этом компьютере.
///
/// На одной машине их обычно несколько: старый профиль после переустановки,
/// второй человек в семье, учётная запись с другим именем. Игру запускали под
/// одним, а программу открыли под другим - и сохранения оказываются "не
/// найдены" при том, что они есть.
///
/// Поэтому папки сохранений ищутся во всех профилях, до которых есть доступ.
/// Чужой профиль обычно закрыт правами, и это нормально: недоступный просто
/// пропускается, прав программа не запрашивает.
/// </summary>
public static class WindowsProfiles
{
    /// <summary>Служебные профили: игр в них не бывает.</summary>
    private static readonly string[] Service =
    [
        "Public", "Default", "Default User", "All Users", "defaultuser0", "WDAGUtilityAccount",
    ];

    /// <summary>Папка своего профиля - с неё начинается любой поиск.</summary>
    public static string Own => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Остальные профили рядом со своим. Свой в список не попадает: его пути
    /// программа берёт у системы, а не собирает из имени папки, потому что
    /// "Документы" могут быть перенесены на другой диск.
    /// </summary>
    public static IReadOnlyList<string> Others()
    {
        var own = Own;
        var root = Path.GetDirectoryName(own);
        if (string.IsNullOrEmpty(root)) return [];

        var found = new List<string>();

        try
        {
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(folder);

                if (string.Equals(folder, own, StringComparison.OrdinalIgnoreCase)) continue;
                if (Service.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                // "Все пользователи" и "Application Data" - не папки, а ссылки,
                // оставленные Windows для совместимости со старыми программами.
                // Они ведут в ProgramData и в обход прав не пускают, зато
                // удлиняют любой список вдвое.
                if (IsLink(folder)) continue;

                found.Add(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return found;
    }

    private static bool IsLink(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Имя профиля, в котором лежит путь, или ничего, если это свой профиль.
    ///
    /// Нужно для подписи в списке: два профиля Windows дают две папки с
    /// одинаковым номером аккаунта Steam, и без имени различить их нельзя.
    /// </summary>
    public static string? NameOf(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var own = Path.GetFullPath(Own);

            if (full.StartsWith(own + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return null;

            foreach (var profile in Others())
            {
                if (full.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileName(profile);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return null;
    }
}
