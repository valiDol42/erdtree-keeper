using Xunit;

namespace ErdtreeKeeper.Core.Tests;

public class SnapshotNamingTests
{
    [Fact]
    public void Keeps_the_pair_suffix_last_when_parts_are_added()
    {
        // Иначе "_before" уезжает в середину и пара "до/после" перестаёт
        // находиться сортировкой по имени.
        var name = SnapshotNaming.WithPairSuffix("Годрик", SnapshotNaming.BeforeSuffix);
        name = SnapshotNaming.Append(name, "Лимгрейв");

        Assert.Equal("Годрик_Лимгрейв_before", name);
    }

    [Fact]
    public void Replaces_the_pair_suffix_instead_of_stacking_it()
    {
        var name = SnapshotNaming.WithPairSuffix("Годрик_before", SnapshotNaming.AfterSuffix);

        Assert.Equal("Годрик_after", name);
    }

    [Fact]
    public void Turns_forbidden_characters_into_spaces_so_words_do_not_stick()
    {
        // В названиях из игры есть двоеточие: "Аспекты Горнила: Рога".
        var part = SnapshotNaming.Sanitize("Аспекты Горнила: Рога");

        Assert.Equal("Аспекты Горнила Рога", part);
    }

    [Fact]
    public void Adds_the_dlc_tag_once()
    {
        var name = SnapshotNaming.EnsureDlcTag("");
        name = SnapshotNaming.EnsureDlcTag(name);
        name = SnapshotNaming.Append(name, "Теневой Альтус");

        Assert.Equal("DLC_Теневой Альтус", name);
    }

    [Fact]
    public void Adds_the_extension_only_once()
    {
        Assert.Equal("Годрик.sl2", SnapshotNaming.ToFileName("Годрик"));
        Assert.Equal("Годрик.sl2", SnapshotNaming.ToFileName("Годрик.sl2"));
        Assert.Equal("", SnapshotNaming.ToFileName("   "));
    }

    [Fact]
    public void Auto_names_carry_the_place_and_the_moment()
    {
        var at = new DateTime(2026, 8, 12, 14, 5, 9);
        var name = SnapshotNaming.AutoName(null, at);

        Assert.Equal("автоснимок_2026-08-12_14-05-09.sl2", name);
    }
}
