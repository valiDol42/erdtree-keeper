using Xunit;

namespace ErdtreeKeeper.Core.Tests;

/// <summary>
/// Игры различаются местом хранения, расширениями и тем, насколько глубоко
/// программа понимает их файлы. Здесь проверяется, что эти различия работают,
/// а не только описаны в таблице.
/// </summary>
public class GameProfileTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "erdtree-keeper-tests", Guid.NewGuid().ToString("N"));

    public GameProfileTests()
    {
        Loc.Current.Language = Lang.Ru;
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Every_built_in_game_is_described_completely()
    {
        foreach (var game in GameProfiles.BuiltIn)
        {
            Assert.False(string.IsNullOrWhiteSpace(game.Id), "у игры нет идентификатора");
            Assert.False(string.IsNullOrWhiteSpace(game.Name), $"{game.Id}: нет названия");
            Assert.False(string.IsNullOrWhiteSpace(game.RootPath), $"{game.Id}: нет папки");
            Assert.NotEmpty(game.Extensions);
            Assert.NotEmpty(game.ProcessNames);
            Assert.NotNull(game.PrimaryFile);
        }

        // Одинаковые идентификаторы означали бы общие папку снимков и выбор.
        Assert.Equal(
            GameProfiles.BuiltIn.Count,
            GameProfiles.BuiltIn.Select(g => g.Id).Distinct().Count());
    }

    /// <summary>
    /// Полную проверку контрольных сумм программа обещает только там, где
    /// действительно умеет её делать. Если этот тест упал, значит обещание
    /// разъехалось с кодом.
    /// </summary>
    [Fact]
    public void Only_elden_ring_claims_full_knowledge()
    {
        Assert.True(GameProfiles.EldenRing.HasMapKnowledge);

        foreach (var game in GameProfiles.BuiltIn.Where(g => g.Id != GameProfiles.EldenRingId))
        {
            Assert.False(game.HasMapKnowledge, $"{game.Id} не должна обещать знание карты");
        }
    }

    [Theory]
    [InlineData("ER0000.sl2", true)]
    [InlineData("ER0000.co2", true)]
    [InlineData("ER0000.sl2.bak", true)]
    [InlineData("DS30000.sl2", true)]
    [InlineData("заметка.txt", false)]
    public void Elden_ring_recognises_its_own_files(string name, bool expected) =>
        Assert.Equal(expected, GameProfiles.EldenRing.LooksLikeSave(name));

    /// <summary>
    /// У игры без объявленных расширений сохранением считается любой файл.
    /// Это сознательный выбор: для незнакомой игры угадывать расширение -
    /// значит показать пустой список там, где файлы есть.
    /// </summary>
    [Fact]
    public void A_game_without_extensions_accepts_any_file()
    {
        var game = Custom(extensions: null);

        Assert.True(game.LooksLikeSave("slot1.dat"));
        Assert.True(game.LooksLikeSave("что-угодно"));
        Assert.Equal(".sav", game.DefaultExtension);
    }

    /// <summary>
    /// У игры бывает несколько мест хранения, и одного пути мало. Dark Souls
    /// Remastered кладёт сейв в "Документы", а не в %APPDATA%, куда его
    /// поместили бы по аналогии с остальными играми FromSoftware - именно на
    /// этом программа и промахнулась, когда путь был один.
    /// </summary>
    [Fact]
    public void Dark_souls_remastered_looks_in_documents_first()
    {
        var game = GameProfiles.BuiltIn.First(g => g.Id == "dark-souls-remastered");

        // Свой профиль проверяется первым, и в нём два места: "Документы" и
        // %APPDATA%. Чужие профили идут следом и здесь не рассматриваются.
        var roots = game.ResolveOwnRoots().ToList();

        Assert.Equal(2, roots.Count);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), roots[0]);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), roots[1]);
        Assert.All(roots, r => Assert.EndsWith(Path.Combine("NBGI", "DARK SOULS REMASTERED"), r));
    }

    /// <summary>
    /// Выбирается тот путь, который есть на диске. Пока не существует ни один,
    /// остаётся основной - человеку надо показать, где программа искала.
    /// </summary>
    [Fact]
    public void The_root_that_exists_wins()
    {
        var existing = Path.Combine(_folder, "есть");
        Directory.CreateDirectory(existing);

        var game = GameProfiles.FromCustom(new CustomGame
        {
            Id = "custom-test",
            Name = "Игра",
            Folder = Path.Combine(_folder, "нет-такой"),
        }) with
        {
            AlternateRoots = [new SaveRoot(SaveRootKind.Custom, existing)],
        };

        Assert.Equal(existing, game.ResolveRoot());

        var nowhere = game with { AlternateRoots = [] };
        Assert.Equal(Path.Combine(_folder, "нет-такой"), nowhere.ResolveRoot());
    }

    /// <summary>Запасные пути игр тоже под запретом для папки снимков.</summary>
    [Fact]
    public void Alternate_game_folders_are_rejected_too()
    {
        var remastered = GameProfiles.BuiltIn.First(g => g.Id == "dark-souls-remastered");

        foreach (var candidate in remastered.ResolveRoots())
        {
            Assert.True(GameSaves.IsInsideGameFolder(candidate), candidate);
        }
    }

    /// <summary>
    /// Профилей Windows на машине бывает несколько, и игру могли запускать не
    /// под тем, под которым открыли программу. Поиск обязан заглянуть и туда.
    /// </summary>
    [Fact]
    public void Other_windows_profiles_are_searched_too()
    {
        var game = GameProfiles.BuiltIn.First(g => g.Id == "dark-souls-3");
        var all = game.ResolveRoots().ToList();
        var mine = game.ResolveOwnRoots().ToList();

        Assert.Equal(2, mine.Count);
        Assert.All(mine, m => Assert.Contains(m, all));

        // На машине без других профилей списки совпадают - это не ошибка.
        var others = WindowsProfiles.Others();
        Assert.Equal(mine.Count + others.Count * mine.Count, all.Count);

        foreach (var profile in others)
        {
            Assert.Contains(all, path => path.StartsWith(profile, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Свой профиль в списке чужих не появляется - его пути берутся у системы.</summary>
    [Fact]
    public void My_own_profile_is_not_listed_as_someone_elses()
    {
        var own = WindowsProfiles.Own;

        Assert.DoesNotContain(WindowsProfiles.Others(),
            p => string.Equals(p, own, StringComparison.OrdinalIgnoreCase));

        Assert.Null(WindowsProfiles.NameOf(Path.Combine(own, "AppData", "Roaming", "EldenRing")));
    }

    /// <summary>Папка, указанная вручную, к профилю не привязана и по профилям не размножается.</summary>
    [Fact]
    public void A_hand_picked_folder_is_not_multiplied_across_profiles()
    {
        var game = GameProfiles.FromCustom(new CustomGame
        {
            Id = "custom-test",
            Name = "Игра",
            Folder = _folder,
        });

        Assert.Equal([_folder], game.ResolveRoots().ToList());
    }

    [Fact]
    public void Extensions_are_parsed_from_a_plain_list()
    {
        Assert.Equal([".sav", ".dat"], GameProfiles.SplitExtensions("sav, .DAT"));
        Assert.Empty(GameProfiles.SplitExtensions("   "));
        Assert.Equal([".sav"], GameProfiles.SplitExtensions(".sav;.sav"));
    }

    /// <summary>Расширение снимка повторяет расширение исходного файла.</summary>
    [Theory]
    [InlineData("ER0000.co2", ".co2")]
    [InlineData("ER0000.sl2", ".sl2")]
    [InlineData("ER0000.sl2.bak", ".sl2")]
    public void Snapshot_extension_follows_the_source(string file, string expected) =>
        Assert.Equal(expected, GameProfiles.EldenRing.ExtensionOf(file));

    /// <summary>
    /// Обычно игра заводит по папке на аккаунт Steam, но не всегда: часть игр
    /// кладёт сейвы прямо в свою папку. Раньше такой случай давал пустой
    /// список при живых файлах внутри.
    /// </summary>
    [Fact]
    public void Saves_lying_in_the_root_are_found_too()
    {
        File.WriteAllBytes(Path.Combine(_folder, "slot1.sav"), [1, 2, 3]);

        var game = Custom(extensions: ".sav");
        var accounts = GameSaves.FindAccounts(game, _folder);

        Assert.Single(accounts);
        Assert.Equal(_folder, accounts[0].Path);
        Assert.Single(GameSaves.FindSaveFiles(game, accounts[0].Path));
    }

    [Fact]
    public void Account_folders_are_found_the_usual_way()
    {
        var account = Path.Combine(_folder, "76561190000000001");
        Directory.CreateDirectory(account);
        File.WriteAllBytes(Path.Combine(account, "slot1.sav"), [1]);

        var accounts = GameSaves.FindAccounts(Custom(extensions: ".sav"), _folder);

        Assert.Single(accounts);
        Assert.Equal("76561190000000001", accounts[0].SteamId);
    }

    /// <summary>Главный файл игры показывается первым - его выбирают почти всегда.</summary>
    [Fact]
    public void The_main_save_comes_first()
    {
        var account = Path.Combine(_folder, "acc");
        Directory.CreateDirectory(account);
        File.WriteAllBytes(Path.Combine(account, "AAA.sl2"), [1]);
        File.WriteAllBytes(Path.Combine(account, "DS30000.sl2"), [1]);
        File.WriteAllBytes(Path.Combine(account, "DS30000.sl2.bak"), [1]);

        var game = GameProfiles.BuiltIn.First(g => g.Id == "dark-souls-3");
        var files = GameSaves.FindSaveFiles(game, account);

        Assert.Equal("DS30000.sl2", files[0].Name);
        Assert.True(files[^1].IsGameBackup);
    }

    /// <summary>
    /// Ротация удаляет только свои автоснимки. У игры со своими расширениями
    /// метка времени опознаётся так же, как у Elden Ring, - иначе чужие копии
    /// попадали бы под удаление.
    /// </summary>
    [Theory]
    [InlineData("Лимгрейв_2026-09-01_12-00-00.sav", true)]
    [InlineData("моя копия.sav", false)]
    public void Auto_names_are_recognised_for_any_game(string name, bool expected) =>
        Assert.Equal(expected, SnapshotNaming.IsAutoName(name, Custom(extensions: ".sav")));

    [Fact]
    public void Auto_names_work_even_without_extensions() =>
        Assert.True(SnapshotNaming.IsAutoName("save_2026-09-01_12-00-00.bin", Custom(extensions: null)));

    [Fact]
    public void Time_can_be_appended_to_a_name()
    {
        var name = SnapshotNaming.AppendTime("Босс", new DateTime(2026, 9, 1, 12, 5, 0));

        Assert.Equal("Босс_2026-09-01_12-05", name);
    }

    /// <summary>
    /// Папка игры под запретом для снимков - для любой известной игры, а не
    /// только для выбранной. Иначе снимки Elden Ring можно было бы сложить в
    /// папку Dark Souls III, где ротация удаляла бы живые сейвы.
    /// </summary>
    [Fact]
    public void Any_known_game_folder_is_rejected()
    {
        foreach (var game in GameProfiles.BuiltIn)
        {
            Assert.True(GameSaves.IsInsideGameFolder(game.ResolveRoot()), game.Id);
            Assert.True(GameSaves.IsInsideGameFolder(Path.Combine(game.ResolveRoot(), "снимки")), game.Id);
        }

        Assert.False(GameSaves.IsInsideGameFolder(_folder));
    }

    /// <summary>Игра без известного имени процесса не притворяется запущенной.</summary>
    [Fact]
    public void A_game_without_a_process_name_is_never_reported_as_running() =>
        Assert.False(GameSaves.IsGameRunning(Custom(extensions: null)));

    private GameProfile Custom(string? extensions) => GameProfiles.FromCustom(new CustomGame
    {
        Id = "custom-test",
        Name = "Игра",
        Folder = _folder,
        Extensions = extensions,
    });
}
