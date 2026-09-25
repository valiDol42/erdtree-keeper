using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErdtreeKeeper.Core;

/// <summary>
/// Игра, добавленная человеком вручную.
///
/// Это не профиль, а то, что от него остаётся на диске: профиль из этой записи
/// собирается при загрузке. Поля простые и с сеттерами - иначе их не прочитать
/// генератором JSON, который в сборке NativeAOT работает вместо отражения.
/// </summary>
public sealed class CustomGame
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Папка с сохранениями. Для облачной - путь до remote внутри userdata.</summary>
    public string? Folder { get; set; }

    /// <summary>Папка найдена в облаке Steam, а не указана вручную. Различие только для подписи.</summary>
    public bool IsSteamCloud { get; set; }

    /// <summary>Идентификатор игры в Steam, если он известен. Нужен для подписи и поиска.</summary>
    public string? AppId { get; set; }

    /// <summary>Расширения через запятую. Пусто - любые файлы.</summary>
    public string? Extensions { get; set; }

    /// <summary>Главный файл сохранения: он выбирается в списке первым.</summary>
    public string? PrimaryFile { get; set; }

    /// <summary>Имя процесса игры - по нему видно, что игра запущена.</summary>
    public string? Process { get; set; }
}

/// <summary>
/// Что программа помнит про одну игру.
///
/// Папки и последний выбор у каждой игры свои: снимки Dark Souls III не должны
/// попадать в список Elden Ring, а удаление по кнопке - задевать чужие файлы.
/// </summary>
public sealed class GameState
{
    public string? SnapshotFolder { get; set; }
    public string? AutoSnapshotFolder { get; set; }
    public string? LastAccountId { get; set; }
    public string? LastFileName { get; set; }
    public string? LastSnapshotName { get; set; }
}

/// <summary>Всё, что приложение запоминает между запусками.</summary>
public sealed class Settings
{
    /// <summary>Игра, выбранная в прошлый раз.</summary>
    public string? SelectedGameId { get; set; }

    /// <summary>Игры, добавленные вручную.</summary>
    public List<CustomGame> CustomGames { get; set; } = [];

    /// <summary>Папки и выбор по каждой игре.</summary>
    public Dictionary<string, GameState> Games { get; set; } = new();

    /// <summary>Подписи аккаунтов: длинный SteamID -> человеческое имя.</summary>
    public Dictionary<string, string> Aliases { get; set; } = new();

    public bool AutoSnapshotEnabled { get; set; }

    /// <summary>
    /// Не чаще одного автосохранения за столько минут.
    ///
    /// Это нижняя граница частоты, а не расписание: снимок всё равно делается
    /// только после того, как игра записала сейв.
    /// </summary>
    public int AutoSnapshotMinutes { get; set; } = 5;

    /// <summary>Сколько автосохранений хранить, прежде чем удалять старые.</summary>
    public int AutoSnapshotKeep { get; set; } = 10;

    public bool OnboardingDone { get; set; }

    /// <summary>Выбранный язык. Пусто - берём из системы при первом запуске.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Разрешено ли программе обращаться к GitHub за обновлениями.
    ///
    /// Три состояния, и третье - главное: пусто означает, что человека ещё не
    /// спрашивали. Без разрешения не уходит ни один запрос, поэтому умолчания
    /// "включено" здесь быть не может.
    /// </summary>
    public bool? UpdatesAllowed { get; set; }

    /// <summary>Проверять при запуске - отдельно от самого разрешения.</summary>
    public bool UpdatesCheckOnStart { get; set; }

    /// <summary>Когда проверяли в последний раз (UTC, ISO-8601).</summary>
    public string? LastUpdateCheck { get; set; }

    /// <summary>Версия, о которой попросили больше не напоминать.</summary>
    public string? SkippedVersion { get; set; }

    // ─── Прежние поля: читаются ради переноса и больше не пишутся ───────

    public string? LastAccountId { get; set; }
    public string? LastFileName { get; set; }
    public string? LastSnapshotName { get; set; }
    public string? SnapshotFolder { get; set; }
    public string? AutoSnapshotFolder { get; set; }
}

[JsonSerializable(typeof(Settings))]
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>
/// Настройки лежат рядом с программой, а не в системных папках.
///
/// Так приложение остаётся переносимым: скопировали папку на флешку - и все
/// настройки уехали вместе с ней, а после удаления папки в системе не остаётся
/// ничего. Это же и вопрос доверия: видно, что программа пишет ровно один файл
/// и ровно там, где лежит сама.
///
/// Если папка программы недоступна для записи (например, программу положили в
/// Program Files), настройки уходят в %APPDATA% - иначе они просто терялись бы
/// молча. Фактический путь всегда показан в окне "О программе".
/// </summary>
public sealed class PortableSettings
{
    private const string FileName = "erdtree-keeper.settings.json";
    private readonly object _lock = new();

    private PortableSettings(string path, Settings values, bool isPortable, string appFolder)
    {
        Path = path;
        Values = values;
        IsPortable = isPortable;
        AppFolderUsed = appFolder;
    }

    /// <summary>Полный путь к файлу настроек.</summary>
    public string Path { get; }

    /// <summary>Настройки лежат рядом с программой, а не в системной папке.</summary>
    public bool IsPortable { get; }

    /// <summary>Папка программы, от которой считаются папки снимков по умолчанию.</summary>
    public string AppFolderUsed { get; }

    public Settings Values { get; }

    /// <summary>Папка, где лежит сама программа.</summary>
    public static string AppFolder => AppContext.BaseDirectory.TrimEnd(
        System.IO.Path.DirectorySeparatorChar);

    /// <summary>
    /// Что лежит по пути настроек прямо сейчас.
    ///
    /// Окно "О программе" показывает не предполагаемый путь, а результат
    /// обращения к диску: есть файл или нет, какого размера и когда изменён.
    /// Иначе это было бы обещание, а не проверяемый факт.
    /// </summary>
    public string DescribeFile()
    {
        try
        {
            var info = new FileInfo(Path);
            return info.Exists
                ? Loc.Get("file.exists", info.Length, info.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss"))
                : Loc.Get("file.missing");
        }
        catch (IOException ex) { return Loc.Get("file.unreadable", ex.Message); }
        catch (UnauthorizedAccessException) { return Loc.Get("file.noAccess"); }
    }

    public static PortableSettings Load() => LoadFrom(AppFolder);

    /// <summary>
    /// То же самое, но для заданной папки программы.
    ///
    /// Существует ради тестов: обычная загрузка привязана к тому, откуда
    /// запущен процесс, и проверить сохранение настроек иначе было бы нечем.
    /// </summary>
    public static PortableSettings LoadFrom(string appFolder)
    {
        var portablePath = System.IO.Path.Combine(appFolder, FileName);
        var portable = CanWriteTo(appFolder);

        var path = portable
            ? portablePath
            : System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ErdtreeKeeper",
                FileName);

        var values = ReadOrDefault(path)
                     // При переезде в системную папку старые портативные
                     // настройки не бросаем - подхватываем, если они там есть.
                     ?? (portable ? null : ReadOrDefault(portablePath))
                     ?? new Settings();

        MigrateSingleGame(values);

        return new PortableSettings(path, values, portable, appFolder);
    }

    /// <summary>
    /// Переносит настройки версий, знавших одну игру.
    ///
    /// До появления выбора игры папка снимков и последний файл лежали прямо в
    /// корне настроек. Теперь у каждой игры своё место, и старые значения
    /// становятся записью про Elden Ring - иначе после обновления человек
    /// увидел бы пустой список там, где у него лежали все копии.
    /// </summary>
    private static void MigrateSingleGame(Settings values)
    {
        var hasOld = values.SnapshotFolder is not null
                     || values.LastAccountId is not null
                     || values.LastFileName is not null
                     || values.LastSnapshotName is not null
                     || values.AutoSnapshotFolder is not null;

        if (hasOld && !values.Games.ContainsKey(GameProfiles.EldenRingId))
        {
            values.Games[GameProfiles.EldenRingId] = new GameState
            {
                SnapshotFolder = values.SnapshotFolder,
                AutoSnapshotFolder = values.AutoSnapshotFolder,
                LastAccountId = values.LastAccountId,
                LastFileName = values.LastFileName,
                LastSnapshotName = values.LastSnapshotName,
            };
        }

        values.SnapshotFolder = null;
        values.AutoSnapshotFolder = null;
        values.LastAccountId = null;
        values.LastFileName = null;
        values.LastSnapshotName = null;

        values.SelectedGameId ??= GameProfiles.EldenRingId;
    }

    /// <summary>
    /// Все игры: встроенные и добавленные вручную.
    ///
    /// Порядок постоянный - встроенные так, как они перечислены в коде, а свои
    /// следом, в порядке добавления.
    /// </summary>
    public IReadOnlyList<GameProfile> AllGames()
    {
        var all = new List<GameProfile>(GameProfiles.BuiltIn);
        all.AddRange(Values.CustomGames.Select(GameProfiles.FromCustom));
        return all;
    }

    /// <summary>Запомненное про игру, с созданием записи при первом обращении.</summary>
    public GameState StateOf(GameProfile game)
    {
        if (Values.Games.TryGetValue(game.Id, out var state)) return state;

        state = new GameState();
        Values.Games[game.Id] = state;
        return state;
    }

    /// <summary>
    /// Папка снимков по умолчанию.
    ///
    /// Для Elden Ring - "Снимки" рядом с программой, как было всегда. Для
    /// остальных игр внутри неё заводится подпапка с названием игры: иначе
    /// копии разных игр смешались бы в одном списке, а удаление по кнопке
    /// задело бы чужие.
    /// </summary>
    public string DefaultSnapshotFolder(GameProfile game) => DefaultSnapshotFolder(game, Language);

    /// <summary>
    /// Папка снимков игры - закреплённая в настройках.
    ///
    /// Папка по умолчанию зависит от языка: "Снимки" или "Snapshots". Если её
    /// не закрепить, смена языка уводит список в другую папку, и снимки,
    /// сделанные до переключения, пропадают из виду. В 1.5.0-1.5.3 так и
    /// было: путь вычислялся заново при каждом запуске, а до 1.5.0 он
    /// записывался в настройки при первой загрузке.
    ///
    /// Путь закрепляется при первом обращении. Если к этому моменту уже есть
    /// папка на другом языке и в ней лежат файлы - её успели создать версии,
    /// где путь не закреплялся, - берётся она: иначе эти снимки так и
    /// остались бы невидимыми.
    /// </summary>
    public string SnapshotFolderOf(GameProfile game)
    {
        var state = StateOf(game);
        if (!string.IsNullOrWhiteSpace(state.SnapshotFolder)) return state.SnapshotFolder;

        var current = DefaultSnapshotFolder(game, Language);
        var other = DefaultSnapshotFolder(game, Language == Lang.Ru ? Lang.En : Lang.Ru);
        var chosen = !HasFiles(current) && HasFiles(other) ? other : current;

        state.SnapshotFolder = chosen;
        Save();
        return chosen;
    }

    private static bool HasFiles(string folder)
    {
        try { return Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public string DefaultSnapshotFolder(GameProfile game, Lang language)
    {
        var root = System.IO.Path.Combine(AppFolderUsed, Loc.Get(language, "path.snapshots"));
        if (game.Id == GameProfiles.EldenRingId) return root;

        var folder = SnapshotNaming.Sanitize(game.Name);
        return System.IO.Path.Combine(root, folder.Length > 0 ? folder : game.Id);
    }

    /// <summary>
    /// Язык из настроек, а без записи - язык системы. Применить его к
    /// интерфейсу должен вызывающий: один раз, при запуске.
    /// </summary>
    public Lang Language => LanguageOf(Values);

    private static Lang LanguageOf(Settings values) =>
        Enum.TryParse<Lang>(values.Language, out var saved) ? saved : Loc.DetectFromSystem();

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Пишем через временный файл: обрыв на середине записи не
                // должен оставлять покорёженные настройки.
                var temp = Path + ".tmp";
                var json = JsonSerializer.Serialize(Values, SettingsJsonContext.Default.Settings);
                File.WriteAllText(temp, json);
                File.Move(temp, Path, overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Settings? ReadOrDefault(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.Settings);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private static bool CanWriteTo(string folder)
    {
        try
        {
            var probe = System.IO.Path.Combine(folder, $".write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
