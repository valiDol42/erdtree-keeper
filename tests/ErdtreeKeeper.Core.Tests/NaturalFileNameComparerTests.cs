using Xunit;

namespace ErdtreeKeeper.Core.Tests;

public class NaturalFileNameComparerTests
{
    private static List<string> Sort(params string[] names) =>
        [.. names.Order(NaturalFileNameComparer.Instance)];

    [Fact]
    public void Compares_numbers_as_numbers_not_character_by_character()
    {
        // Посимвольно "#12" встаёт перед "#2", и список номеров рассыпается.
        var sorted = Sort("#12 Босс.sl2", "#2 Босс.sl2", "#1 Босс.sl2", "#100 Босс.sl2");

        Assert.Equal(["#1 Босс.sl2", "#2 Босс.sl2", "#12 Босс.sl2", "#100 Босс.sl2"], sorted);
    }

    [Fact]
    public void Leading_zeros_do_not_change_the_order()
    {
        var sorted = Sort("снимок_007.sl2", "снимок_8.sl2", "снимок_06.sl2");

        Assert.Equal(["снимок_06.sl2", "снимок_007.sl2", "снимок_8.sl2"], sorted);
    }

    [Fact]
    public void Latin_comes_before_cyrillic_the_way_explorer_shows_it()
    {
        // Порядковое сравнение, а не по локали: у собранного exe включён
        // InvariantGlobalization, и порядок должен совпадать везде.
        var sorted = Sort("Лимгрейв.sl2", "DLC_Теневой Альтус.sl2", "DLC_Нижний поток.sl2");

        Assert.Equal(["DLC_Нижний поток.sl2", "DLC_Теневой Альтус.sl2", "Лимгрейв.sl2"], sorted);
    }

    [Fact]
    public void Case_does_not_matter()
    {
        Assert.Equal(0, NaturalFileNameComparer.Instance.Compare("Годрик.sl2", "ГОДРИК.SL2"));
    }

    [Fact]
    public void Pairs_stay_together_and_before_comes_first()
    {
        var sorted = Sort(
            "Годрик_before.sl2", "Годрик_after.sl2",
            "Марго_before.sl2", "Марго_after.sl2");

        Assert.Equal(
            ["Годрик_after.sl2", "Годрик_before.sl2", "Марго_after.sl2", "Марго_before.sl2"],
            sorted);
    }

    [Fact]
    public void Handles_timestamps_in_autosave_names()
    {
        var sorted = Sort(
            "Лимгрейв_2026-08-12_10-31-02.sl2",
            "Лимгрейв_2026-08-12_09-05-40.sl2",
            "Лимгрейв_2026-08-09_23-59-00.sl2");

        Assert.Equal(
            [
                "Лимгрейв_2026-08-09_23-59-00.sl2",
                "Лимгрейв_2026-08-12_09-05-40.sl2",
                "Лимгрейв_2026-08-12_10-31-02.sl2",
            ],
            sorted);
    }

    [Fact]
    public void Survives_nulls_and_empty_names()
    {
        Assert.Equal(0, NaturalFileNameComparer.Instance.Compare(null, null));
        Assert.True(NaturalFileNameComparer.Instance.Compare(null, "а") < 0);
        Assert.True(NaturalFileNameComparer.Instance.Compare("а", null) > 0);
        Assert.True(NaturalFileNameComparer.Instance.Compare("", "а") < 0);
    }
}
