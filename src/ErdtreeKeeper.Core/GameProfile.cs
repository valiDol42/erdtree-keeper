namespace ErdtreeKeeper.Core;

/// <summary>
/// Насколько глубоко программа понимает формат сейва.
///
/// От этого зависит ровно одно: что честно сказать игроку о проверке. Сообщить,
/// что сошлись контрольные суммы, там, где проверена только структура, - хуже,
/// чем не проверять вовсе.
/// </summary>
public enum SaveLayout
{
    /// <summary>Elden Ring: 10 слотов и профиль, у каждого своя MD5. Проверяется полностью.</summary>
    EldenRing,

    /// <summary>Контейнер BND4 (Dark Souls II и III, Sekiro, Armored Core VI, Nightreign): проверяется структура.</summary>
    Bnd4,

    /// <summary>Формат неизвестен: сверяется только точность копии по SHA-256.</summary>
    Opaque,
}

/// <summary>Откуда берётся корневая папка сохранений.</summary>
public enum SaveRootKind
{
    /// <summary>Подпапка %APPDATA%.</summary>
    AppData,

    /// <summary>Подпапка "Документы".</summary>
    Documents,

    /// <summary>Облачная папка Steam: userdata/&lt;аккаунт&gt;/&lt;appid&gt;/remote.</summary>
    SteamCloud,

    /// <summary>Папка, указанная человеком.</summary>
    Custom,
}

/// <summary>
/// Игра, сохранения которой программа умеет копировать.
///
/// Всё, что раньше было зашито под Elden Ring - путь, расширения, имя процесса,
/// способ проверки, - собрано здесь. Добавить игру значит добавить запись, а не
/// править логику: на этом же механизме работают и игры, добавленные вручную.
/// </summary>
public sealed record GameProfile(
    string Id,
    string Name,
    SaveRootKind RootKind,
    string RootPath,
    IReadOnlyList<string> Extensions,
    string? PrimaryFile,
    IReadOnlyList<string> ProcessNames,
    SaveLayout Layout,
    bool BuiltIn = true)
{
    /// <summary>Игра, для которой программа знает карту и называет снимки местом.</summary>
    public bool HasMapKnowledge => Layout == SaveLayout.EldenRing;

    /// <summary>Расширение по умолчанию для имени снимка.</summary>
    public string DefaultExtension => Extensions.Count > 0 ? Extensions[0] : ".sav";

    /// <summary>
    /// Корневая папка сохранений на этом компьютере.
    ///
    /// Путь вычисляется, а не хранится: в нём стоит имя пользователя Windows,
    /// и путь, записанный на чужой машине, здесь бы не подошёл.
    /// </summary>
    public string ResolveRoot() => RootKind switch
    {
        SaveRootKind.AppData => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), RootPath),
        SaveRootKind.Documents => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), RootPath),
        _ => RootPath,
    };

    /// <summary>Похож ли файл на сохранение этой игры.</summary>
    public bool LooksLikeSave(string fileName)
    {
        // Пустой список расширений - согласие на любой файл. Так добавляются
        // игры, о формате которых неизвестно ничего, а копировать надо.
        if (Extensions.Count == 0) return true;

        foreach (var ext in Extensions)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;

            // Резервные копии, которые делает сама игра.
            if (fileName.EndsWith(ext + ".bak", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>Расширение файла без учёта ".bak" - им называется снимок.</summary>
    public string ExtensionOf(string fileName)
    {
        foreach (var ext in Extensions)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return ext;
            if (fileName.EndsWith(ext + ".bak", StringComparison.OrdinalIgnoreCase)) return ext;
        }

        var actual = Path.GetExtension(fileName);
        return actual.Length > 0 ? actual : DefaultExtension;
    }
}

/// <summary>
/// Встроенный список игр.
///
/// Все они хранят сейвы одинаково: папка игры, внутри - папка на аккаунт Steam,
/// внутри - сам файл. Поэтому одна и та же работа подходит им всем, и отличия
/// сведены к таблице ниже.
/// </summary>
public static class GameProfiles
{
    public const string EldenRingId = "elden-ring";

    /// <summary>
    /// Игры, о которых программа знает без настройки.
    ///
    /// Пути отвечают тому, где эти игры на Windows держат сейвы. Если игра
    /// стоит не там, её всегда можно добавить вручную - поиск папки от этого
    /// списка не зависит.
    /// </summary>
    public static readonly IReadOnlyList<GameProfile> BuiltIn =
    [
        new(EldenRingId, "Elden Ring", SaveRootKind.AppData, "EldenRing",
            [".sl2", ".co2"], "ER0000.sl2",
            ["eldenring", "start_protected_game"], SaveLayout.EldenRing),

        new("nightreign", "Elden Ring Nightreign", SaveRootKind.AppData, "Nightreign",
            [".sl2", ".co2"], "NR0000.sl2",
            ["nightreign", "start_protected_game"], SaveLayout.Bnd4),

        new("dark-souls-remastered", "Dark Souls Remastered", SaveRootKind.AppData,
            Path.Combine("NBGI", "DARK SOULS REMASTERED"),
            [".sl2", ".sdt"], "DRAKS0005.sl2",
            ["DarkSoulsRemastered"], SaveLayout.Bnd4),

        new("dark-souls-ptde", "Dark Souls: Prepare to Die Edition", SaveRootKind.Documents,
            Path.Combine("NBGI", "DarkSouls"),
            [".sdt"], "DRAKS0005.sdt",
            ["DARKSOULS"], SaveLayout.Opaque),

        new("dark-souls-2", "Dark Souls II", SaveRootKind.AppData, "DarkSoulsII",
            [".sl2"], "DARKSII0000.sl2",
            ["DarkSoulsII"], SaveLayout.Bnd4),

        new("dark-souls-3", "Dark Souls III", SaveRootKind.AppData, "DarkSoulsIII",
            [".sl2"], "DS30000.sl2",
            ["DarkSoulsIII"], SaveLayout.Bnd4),

        new("sekiro", "Sekiro: Shadows Die Twice", SaveRootKind.AppData, "Sekiro",
            [".sl2"], "S0000.sl2",
            ["sekiro"], SaveLayout.Bnd4),

        new("armored-core-6", "Armored Core VI", SaveRootKind.AppData, "ArmoredCore6",
            [".sl2"], "AC60000.sl2",
            ["armoredcore6", "start_protected_game"], SaveLayout.Bnd4),
    ];

    public static GameProfile EldenRing => BuiltIn[0];

    /// <summary>Профиль по идентификатору. Неизвестный - Elden Ring: с него всё начиналось.</summary>
    public static GameProfile ById(IEnumerable<GameProfile> all, string? id) =>
        all.FirstOrDefault(g => g.Id == id) ?? EldenRing;

    /// <summary>
    /// Игра, добавленная человеком, - в виде профиля.
    ///
    /// Идентификатор придумывается один раз и живёт в настройках: по нему
    /// привязаны папка снимков и последний выбранный файл.
    /// </summary>
    public static GameProfile FromCustom(CustomGame game) => new(
        string.IsNullOrWhiteSpace(game.Id) ? NewCustomId() : game.Id,
        string.IsNullOrWhiteSpace(game.Name) ? game.Id : game.Name,
        game.IsSteamCloud ? SaveRootKind.SteamCloud : SaveRootKind.Custom,
        game.Folder ?? "",
        SplitExtensions(game.Extensions),
        string.IsNullOrWhiteSpace(game.PrimaryFile) ? null : game.PrimaryFile,
        string.IsNullOrWhiteSpace(game.Process) ? [] : [game.Process!],
        SaveLayout.Opaque,
        BuiltIn: false);

    public static string NewCustomId() => "custom-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Расширения из строки вида ".sav, .dat".
    ///
    /// Пустая строка означает "любой файл": для незнакомой игры это честнее,
    /// чем угадать расширение и показать пустой список.
    /// </summary>
    public static IReadOnlyList<string> SplitExtensions(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        return value
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.StartsWith('.') ? part : "." + part)
            .Select(part => part.ToLowerInvariant())
            .Distinct()
            .ToList();
    }
}
