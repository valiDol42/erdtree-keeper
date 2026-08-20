using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErdtreeKeeper.Core;

/// <summary>Всё, что приложение запоминает между запусками.</summary>
public sealed class Settings
{
    public string? LastAccountId { get; set; }
    public string? LastFileName { get; set; }
    public string? LastSnapshotName { get; set; }

    /// <summary>Куда складывать снимки. По умолчанию - папка рядом с программой.</summary>
    public string? SnapshotFolder { get; set; }

    /// <summary>Подписи аккаунтов: длинный SteamID -> человеческое имя.</summary>
    public Dictionary<string, string> Aliases { get; set; } = new();

    public bool AutoSnapshotEnabled { get; set; }

    /// <summary>Куда складывать автосохранения. Пусто - подпапка рядом со снимками.</summary>
    public string? AutoSnapshotFolder { get; set; }

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
}

[JsonSerializable(typeof(Settings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>
/// Настройки лежат рядом с программой, а не в системных папках.
///
/// Так приложение остаётся переносимым: скопировали папку на флешку - и все
/// настройки уехали вместе с ней, а после удаления папки в системе не остаётся
/// ничего. Это же и вопрос доверия: видно, что программа пишет ровно один файл
/// и ровно там, где лежит сама.
///
/// Если папка programme недоступна для записи (например, программу положили в
/// Program Files), настройки уходят в %APPDATA% - иначе они просто терялись бы
/// молча. Фактический путь всегда показан в окне "О программе".
/// </summary>
public sealed class PortableSettings
{
    private const string FileName = "erdtree-keeper.settings.json";
    private readonly object _lock = new();

    private PortableSettings(string path, Settings values, bool isPortable)
    {
        Path = path;
        Values = values;
        IsPortable = isPortable;
    }

    /// <summary>Полный путь к файлу настроек.</summary>
    public string Path { get; }

    /// <summary>Настройки лежат рядом с программой, а не в системной папке.</summary>
    public bool IsPortable { get; }

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

        values.SnapshotFolder ??= System.IO.Path.Combine(appFolder, Loc.Get("path.snapshots"));

        return new PortableSettings(path, values, portable);
    }

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
