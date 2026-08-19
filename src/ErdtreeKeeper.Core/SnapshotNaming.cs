using System.Text.RegularExpressions;

namespace ErdtreeKeeper.Core;

/// <summary>
/// Сборка имени снимка по частям.
///
/// Имя набирается кнопками, а не руками: так в файлах не расходится написание
/// одного и того же места, и пары "до/после" всегда находятся рядом при
/// сортировке.
/// </summary>
public static partial class SnapshotNaming
{
    public const string BeforeSuffix = "_before";
    public const string AfterSuffix = "_after";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace { get; }

    [GeneratedRegex(@"_(before|after)$", RegexOptions.IgnoreCase)]
    private static partial Regex PairSuffix { get; }

    /// <summary>
    /// Приводит кусок названия к виду, годному для имени файла.
    ///
    /// Запрещённые символы меняем на пробел, а не выбрасываем: в названиях из
    /// игры встречается двоеточие ("Аспекты Горнила: Рога"), и от простого
    /// удаления слова слиплись бы.
    /// </summary>
    public static string Sanitize(string part)
    {
        if (string.IsNullOrWhiteSpace(part)) return "";

        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            part = part.Replace(ch, ' ');
        }

        return Whitespace.Replace(part, " ").Trim();
    }

    /// <summary>
    /// Дописывает часть к имени, сохраняя суффикс пары последним.
    /// Иначе "_before" уехал бы в середину и пары перестали находиться.
    /// </summary>
    public static string Append(string current, string part)
    {
        part = Sanitize(part);
        if (part.Length == 0) return current;

        var name = StripExtension(current).Trim();

        var suffix = "";
        var match = PairSuffix.Match(name);
        if (match.Success)
        {
            suffix = match.Value;
            name = name[..match.Index];
        }

        if (name.Length > 0 && !name.EndsWith('_')) name += "_";

        return $"{name}{part}{suffix}";
    }

    /// <summary>Ставит или заменяет суффикс пары "до/после".</summary>
    public static string WithPairSuffix(string current, string suffix)
    {
        var name = PairSuffix.Replace(StripExtension(current).Trim(), "");
        return $"{name}{suffix}";
    }

    /// <summary>Метка DLC идёт первой частью - так Земли Теней отделяются при сортировке.</summary>
    public static string EnsureDlcTag(string current)
    {
        var name = StripExtension(current);
        return DlcTag().IsMatch(name) ? current : Append(current, "DLC");
    }

    [GeneratedRegex(@"(^|_)DLC(_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DlcTag();

    /// <summary>Готовое имя файла: с расширением и без пустот.</summary>
    public static string ToFileName(string current, string extension = ".sl2")
    {
        var name = StripExtension(current).Trim();
        if (name.Length == 0) return "";
        return name + extension;
    }

    [GeneratedRegex(@"_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$", RegexOptions.IgnoreCase)]
    private static partial Regex AutoNameStamp();

    /// <summary>
    /// Похоже ли имя на автоснимок, сделанный этой программой.
    ///
    /// По этому признаку ротация решает, что можно удалять. Без него она
    /// сносила любые .sl2 в папке - включая отобранные вручную копии и живой
    /// сейв игры, если папку автосохранений навели на папку игры.
    /// </summary>
    public static bool IsAutoName(string fileName)
    {
        foreach (var ext in GameSaves.SaveExtensions)
        {
            if (!fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;
            return AutoNameStamp().IsMatch(fileName[..^ext.Length]);
        }

        return false;
    }

    /// <summary>Имя для автоснимка: место и время, чтобы список читался сам по себе.</summary>
    public static string AutoName(SaveContext? context, DateTime at, string extension = ".sl2")
    {
        var place = Sanitize(context?.Location?.Ru ?? "автоснимок");
        if (place.Length == 0) place = "автоснимок";
        if (context?.IsDlc == true) place = $"DLC_{place}";

        return $"{place}_{at:yyyy-MM-dd_HH-mm-ss}{extension}";
    }

    private static string StripExtension(string value)
    {
        foreach (var ext in GameSaves.SaveExtensions)
        {
            if (value.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return value[..^ext.Length];
        }

        return value;
    }
}
