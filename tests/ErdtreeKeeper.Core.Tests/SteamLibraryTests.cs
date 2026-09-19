using Xunit;

namespace ErdtreeKeeper.Core.Tests;

/// <summary>
/// Список игр Steam читается из его собственных файлов. Формат чужой, и
/// единственная защита от его изменения - разбор, проверенный на настоящих
/// образцах, а не на том, как он должен выглядеть по памяти.
/// </summary>
public class SteamLibraryTests
{
    /// <summary>Библиотеки перечислены в libraryfolders.vdf, и их бывает несколько.</summary>
    [Fact]
    public void Library_paths_are_read_from_the_index()
    {
        var paths = SteamLibrary.ReadLibraryPaths("""
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                    "apps"
                    {
                        "1245620"		"57896237559"
                    }
                }
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                    "apps"
                    {
                        "570"		"1234"
                    }
                }
            }
            """);

        Assert.Equal(2, paths.Count);
        Assert.Contains(@"C:\Program Files (x86)\Steam", paths);
        Assert.Contains(@"D:\SteamLibrary", paths);
    }

    [Fact]
    public void An_empty_or_broken_index_gives_nothing()
    {
        Assert.Empty(SteamLibrary.ReadLibraryPaths(null));
        Assert.Empty(SteamLibrary.ReadLibraryPaths(""));
        Assert.Empty(SteamLibrary.ReadLibraryPaths("{ мусор без кавычек }"));
    }

    [Fact]
    public void A_game_is_read_from_its_manifest()
    {
        var game = SteamLibrary.ReadManifest("""
            "AppState"
            {
            	"appid"		"1245620"
            	"Universe"		"1"
            	"name"		"ELDEN RING"
            	"StateFlags"		"4"
            	"installdir"		"ELDEN RING"
            	"UserConfig"
            	{
            		"name"		"что-то другое"
            	}
            }
            """);

        Assert.NotNull(game);
        Assert.Equal("1245620", game.AppId);

        // Имя берётся первое: ниже в файле встречается ещё одно, чужое.
        Assert.Equal("ELDEN RING", game.Name);
        Assert.Equal("ELDEN RING", game.InstallDir);
        Assert.False(game.HasCloudSaves);
    }

    [Fact]
    public void A_manifest_without_an_id_is_useless()
    {
        Assert.Null(SteamLibrary.ReadManifest(null));
        Assert.Null(SteamLibrary.ReadManifest("\"AppState\" { \"name\" \"Игра\" }"));
    }

    /// <summary>
    /// В путях Steam экранирует обратную косую, и без разэкранирования папка
    /// "D:\\SteamLibrary" не нашлась бы на диске.
    /// </summary>
    [Fact]
    public void Escaped_backslashes_become_real_paths()
    {
        var pairs = Vdf.Pairs("\"path\"\t\t\"E:\\\\Игры\\\\Steam\"").ToList();

        Assert.Single(pairs);
        Assert.Equal(@"E:\Игры\Steam", pairs[0].Value);
    }

    /// <summary>За ключом может идти блок, а не значение - такая пара не считается.</summary>
    [Fact]
    public void A_key_followed_by_a_block_is_not_a_pair()
    {
        var pairs = Vdf.Pairs("""
            "root"
            {
                "ключ"		"значение"
            }
            """).ToList();

        Assert.Single(pairs);
        Assert.Equal("ключ", pairs[0].Key);
        Assert.Equal("значение", pairs[0].Value);
    }

    /// <summary>
    /// Поиск по диску ничего не ломает и не падает, даже когда Steam не
    /// установлен: список просто пустой.
    /// </summary>
    [Fact]
    public void Looking_for_steam_never_throws()
    {
        var root = SteamLibrary.FindRoot();

        if (root is null)
        {
            Assert.Empty(SteamLibrary.Libraries());
            Assert.Empty(SteamLibrary.InstalledGames());
            return;
        }

        Assert.True(Directory.Exists(root));
        Assert.NotEmpty(SteamLibrary.Libraries());
    }
}
