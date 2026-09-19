using Xunit;

namespace ErdtreeKeeper.Core.Tests;

/// <summary>
/// Настройки - то, что пользователь замечает первым, если оно ломается.
/// Здесь проверяется, что они переживают перезапуск и порчу файла.
/// </summary>
public class PortableSettingsTests : IDisposable
{
    // Настоящие SteamID64 начинаются с 76561197960265729 - всё, что ниже, не
    // принадлежит и не может принадлежать ни одному аккаунту. В тестах стоят
    // именно такие: реальный номер из репозитория резолвится в чужой профиль.
    private const string FakeSteamId = "76561190000000001";
    private const string OtherFakeSteamId = "76561190000000002";

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "erdtree-keeper-tests", Guid.NewGuid().ToString("N"));

    public PortableSettingsTests()
    {
        // Имя папки по умолчанию переводится, а проверяется здесь русское.
        Loc.Current.Language = Lang.Ru;
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Settings_survive_a_restart()
    {
        var first = PortableSettings.LoadFrom(_folder);
        first.StateOf(GameProfiles.EldenRing).LastAccountId = FakeSteamId;
        first.Values.Aliases[FakeSteamId] = "Основной";
        first.Values.AutoSnapshotEnabled = true;
        first.Values.AutoSnapshotMinutes = 12;
        first.Values.AutoSnapshotKeep = 42;
        first.Values.OnboardingDone = true;
        first.Save();

        // Так же, как при следующем запуске программы.
        var second = PortableSettings.LoadFrom(_folder);

        Assert.Equal(FakeSteamId, second.StateOf(GameProfiles.EldenRing).LastAccountId);
        Assert.Equal("Основной", second.Values.Aliases[FakeSteamId]);
        Assert.True(second.Values.AutoSnapshotEnabled);
        Assert.Equal(12, second.Values.AutoSnapshotMinutes);
        Assert.Equal(42, second.Values.AutoSnapshotKeep);
        Assert.True(second.Values.OnboardingDone);
    }

    [Fact]
    public void Settings_land_next_to_the_program()
    {
        var settings = PortableSettings.LoadFrom(_folder);
        settings.Save();

        Assert.True(settings.IsPortable);
        Assert.Equal(_folder, Path.GetDirectoryName(settings.Path));
        Assert.True(File.Exists(settings.Path), "файл настроек не появился рядом с программой");
    }

    [Fact]
    public void Fresh_install_gets_the_documented_defaults()
    {
        File.WriteAllText(
            Path.Combine(_folder, "erdtree-keeper.settings.json"),
            "{\"Language\":\"Ru\"}");

        var settings = PortableSettings.LoadFrom(_folder);

        // Значения обещаны в README, поэтому проверяются здесь.
        Assert.Equal(5, settings.Values.AutoSnapshotMinutes);
        Assert.Equal(10, settings.Values.AutoSnapshotKeep);
        Assert.False(settings.Values.AutoSnapshotEnabled);
        Assert.Equal(Path.Combine(_folder, "Снимки"), settings.DefaultSnapshotFolder(GameProfiles.EldenRing));
    }

    /// <summary>
    /// Имя папки по умолчанию берётся из перевода, а язык - из этих же
    /// настроек. Порядок важен: раньше папка успевала получить русское имя до
    /// того, как язык вообще прочитан, и англоязычный игрок получал "Снимки".
    /// </summary>
    [Theory]
    [InlineData("Ru", "Снимки")]
    [InlineData("En", "Snapshots")]
    public void Default_snapshot_folder_follows_the_saved_language(string language, string folder)
    {
        File.WriteAllText(
            Path.Combine(_folder, "erdtree-keeper.settings.json"),
            $"{{\"Language\":\"{language}\"}}");

        var settings = PortableSettings.LoadFrom(_folder);

        Assert.Equal(Path.Combine(_folder, folder), settings.DefaultSnapshotFolder(GameProfiles.EldenRing));
        Assert.Equal(language, settings.Language.ToString());
    }

    /// <summary>
    /// Чтение настроек не переключает язык приложения. Когда переключало,
    /// любой повторный вызов менял язык уже открытому окну, и половина строк
    /// оставалась на прежнем: разметка обновлялась, а значения свойств нет.
    /// Язык применяет тот, кто настройки заказывал, и делает это один раз.
    /// </summary>
    [Theory]
    [InlineData("Ru")]
    [InlineData("En")]
    public void Reading_settings_does_not_switch_the_language(string language)
    {
        File.WriteAllText(
            Path.Combine(_folder, "erdtree-keeper.settings.json"),
            $"{{\"Language\":\"{language}\"}}");

        Loc.Current.Language = Lang.Ru;
        var settings = PortableSettings.LoadFrom(_folder);

        Assert.Equal(Lang.Ru, Loc.Current.Language);
        Assert.Equal(language, settings.Language.ToString());
    }

    /// <summary>Выбор языка переживает перезапуск - он лежит в том же файле.</summary>
    [Fact]
    public void The_chosen_language_survives_a_restart()
    {
        var first = PortableSettings.LoadFrom(_folder);
        first.Values.Language = nameof(Lang.En);
        first.Save();

        var second = PortableSettings.LoadFrom(_folder);

        Assert.Equal(Lang.En, second.Language);
    }

    [Fact]
    public void A_broken_file_does_not_take_the_program_down_with_it()
    {
        var settings = PortableSettings.LoadFrom(_folder);
        File.WriteAllText(settings.Path, "{ это не json");

        var reloaded = PortableSettings.LoadFrom(_folder);

        Assert.Equal(5, reloaded.Values.AutoSnapshotMinutes);
        Assert.Null(reloaded.StateOf(GameProfiles.EldenRing).LastAccountId);
    }

    [Fact]
    public void An_older_file_without_the_newer_fields_still_loads()
    {
        // Файл от версии, где автосохранения ещё не настраивались.
        var path = Path.Combine(_folder, "erdtree-keeper.settings.json");
        File.WriteAllText(path, $$"""
            {
              "LastAccountId": "{{OtherFakeSteamId}}",
              "Aliases": { "{{OtherFakeSteamId}}": "второй" },
              "OnboardingDone": true
            }
            """);

        var settings = PortableSettings.LoadFrom(_folder);

        // Поле переехало к Elden Ring: до появления выбора игры оно лежало
        // в корне настроек, и после обновления список не должен опустеть.
        Assert.Equal(OtherFakeSteamId, settings.StateOf(GameProfiles.EldenRing).LastAccountId);
        Assert.Equal("второй", settings.Values.Aliases[OtherFakeSteamId]);
        Assert.Equal(5, settings.Values.AutoSnapshotMinutes);
        Assert.Equal(10, settings.Values.AutoSnapshotKeep);
    }

    [Fact]
    public void Saving_twice_leaves_no_temporary_file_behind()
    {
        var settings = PortableSettings.LoadFrom(_folder);
        settings.Save();
        settings.StateOf(GameProfiles.EldenRing).LastFileName = "ER0000.sl2";
        settings.Save();

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
        Assert.Equal("ER0000.sl2",
            PortableSettings.LoadFrom(_folder).StateOf(GameProfiles.EldenRing).LastFileName);
    }

    [Fact]
    public void Falls_back_to_appdata_when_it_cannot_write_next_to_itself()
    {
        // Так выглядит установка в Program Files: писать рядом с собой нельзя.
        // README обещает, что настройки тогда уезжают в AppData, а программа
        // продолжает работать - проверяем именно это, а не только код возврата.
        var unwritable = Path.Combine(_folder, "нет-такой-папки", "и-этой-тоже");

        var settings = PortableSettings.LoadFrom(unwritable);

        Assert.False(settings.IsPortable);
        Assert.Contains("ErdtreeKeeper", settings.Path);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            settings.Path);

        // И сохранение туда действительно проходит.
        // Пишем в настоящую пользовательскую папку, поэтому убираем за собой -
        // и только если её до нас там не было.
        var appFolder = Path.GetDirectoryName(settings.Path)!;
        var existedBefore = Directory.Exists(appFolder);

        try
        {
            settings.StateOf(GameProfiles.EldenRing).LastFileName = "ER0000.sl2";
            settings.Save();
            Assert.Equal("ER0000.sl2",
                PortableSettings.LoadFrom(unwritable).StateOf(GameProfiles.EldenRing).LastFileName);
        }
        finally
        {
            if (!existedBefore && Directory.Exists(appFolder))
            {
                try { Directory.Delete(appFolder, recursive: true); } catch (IOException) { }
            }
        }
    }

    [Fact]
    public void Reports_what_is_actually_on_disk()
    {
        var settings = PortableSettings.LoadFrom(_folder);

        // Чтение настроек применяет язык, а без файла берёт его у системы -
        // на англоязычной машине строки ниже были бы английскими.
        Loc.Current.Language = Lang.Ru;

        Assert.Contains("файла ещё нет", settings.DescribeFile());

        settings.Save();
        Assert.Contains("файл есть", settings.DescribeFile());
    }

    /// <summary>
    /// Папки у игр разные, и это главное, ради чего они разведены: список
    /// снимков Dark Souls не должен показывать копии Elden Ring, а удаление
    /// по кнопке - задевать их.
    /// </summary>
    [Fact]
    public void Each_game_keeps_its_own_folder()
    {
        var settings = PortableSettings.LoadFrom(_folder);

        var eldenRing = settings.DefaultSnapshotFolder(GameProfiles.EldenRing);
        var darkSouls = settings.DefaultSnapshotFolder(
            GameProfiles.BuiltIn.First(g => g.Id == "dark-souls-3"));

        Assert.NotEqual(eldenRing, darkSouls);
        Assert.StartsWith(eldenRing, darkSouls);
    }

    /// <summary>
    /// Добавленная игра переживает перезапуск вместе со своей папкой: иначе
    /// её пришлось бы добавлять заново при каждом запуске.
    /// </summary>
    [Fact]
    public void An_added_game_survives_a_restart()
    {
        var first = PortableSettings.LoadFrom(_folder);
        first.Values.CustomGames.Add(new CustomGame
        {
            Id = "custom-test",
            Name = "Nioh 2",
            Folder = _folder,
            Extensions = ".sav",
        });
        first.StateOf(GameProfiles.FromCustom(first.Values.CustomGames[0])).LastFileName = "slot1.sav";
        first.Save();

        var second = PortableSettings.LoadFrom(_folder);
        var game = second.AllGames().Single(g => !g.BuiltIn);

        Assert.Equal("Nioh 2", game.Name);
        Assert.Equal(_folder, game.ResolveRoot());
        Assert.Equal([".sav"], game.Extensions);
        Assert.Equal("slot1.sav", second.StateOf(game).LastFileName);
    }

    /// <summary>
    /// Разрешения на выход в сеть по умолчанию нет - и это именно "не
    /// спрашивали", а не "запрещено": окно обновлений различает эти случаи.
    /// </summary>
    [Fact]
    public void Network_permission_starts_as_unasked()
    {
        var settings = PortableSettings.LoadFrom(_folder);

        Assert.Null(settings.Values.UpdatesAllowed);
        Assert.False(settings.Values.UpdatesCheckOnStart);

        settings.Values.UpdatesAllowed = true;
        settings.Save();

        Assert.True(PortableSettings.LoadFrom(_folder).Values.UpdatesAllowed);
    }
}
