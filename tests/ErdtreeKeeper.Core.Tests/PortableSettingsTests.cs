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

    public PortableSettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Settings_survive_a_restart()
    {
        var first = PortableSettings.LoadFrom(_folder);
        first.Values.LastAccountId = FakeSteamId;
        first.Values.Aliases[FakeSteamId] = "Основной";
        first.Values.AutoSnapshotEnabled = true;
        first.Values.AutoSnapshotMinutes = 12;
        first.Values.AutoSnapshotKeep = 42;
        first.Values.OnboardingDone = true;
        first.Save();

        // Так же, как при следующем запуске программы.
        var second = PortableSettings.LoadFrom(_folder);

        Assert.Equal(FakeSteamId, second.Values.LastAccountId);
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
        var settings = PortableSettings.LoadFrom(_folder);

        // Значения обещаны в README, поэтому проверяются здесь.
        Assert.Equal(5, settings.Values.AutoSnapshotMinutes);
        Assert.Equal(10, settings.Values.AutoSnapshotKeep);
        Assert.False(settings.Values.AutoSnapshotEnabled);
        Assert.Equal(Path.Combine(_folder, "Снимки"), settings.Values.SnapshotFolder);
    }

    [Fact]
    public void A_broken_file_does_not_take_the_program_down_with_it()
    {
        var settings = PortableSettings.LoadFrom(_folder);
        File.WriteAllText(settings.Path, "{ это не json");

        var reloaded = PortableSettings.LoadFrom(_folder);

        Assert.Equal(5, reloaded.Values.AutoSnapshotMinutes);
        Assert.Null(reloaded.Values.LastAccountId);
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

        Assert.Equal(OtherFakeSteamId, settings.Values.LastAccountId);
        Assert.Equal("второй", settings.Values.Aliases[OtherFakeSteamId]);
        Assert.Equal(5, settings.Values.AutoSnapshotMinutes);
        Assert.Equal(10, settings.Values.AutoSnapshotKeep);
    }

    [Fact]
    public void Saving_twice_leaves_no_temporary_file_behind()
    {
        var settings = PortableSettings.LoadFrom(_folder);
        settings.Save();
        settings.Values.LastFileName = "ER0000.sl2";
        settings.Save();

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
        Assert.Equal("ER0000.sl2", PortableSettings.LoadFrom(_folder).Values.LastFileName);
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
            settings.Values.LastFileName = "ER0000.sl2";
            settings.Save();
            Assert.Equal("ER0000.sl2", PortableSettings.LoadFrom(unwritable).Values.LastFileName);
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
        Assert.Contains("файла ещё нет", settings.DescribeFile());

        settings.Save();
        Assert.Contains("файл есть", settings.DescribeFile());
    }
}
