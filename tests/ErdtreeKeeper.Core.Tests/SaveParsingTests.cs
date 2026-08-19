using Xunit;

namespace ErdtreeKeeper.Core.Tests;

/// <summary>
/// Проверка разборщика на настоящих сейвах.
///
/// Эталонные значения сняты независимым разборщиком проекта Erdtree Compass
/// (scripts/_save-context.ts) на тех же файлах. Совпадение имени локации,
/// босса и расстояния до метра означает, что порт на C# читает структуру так
/// же, а не "примерно похоже".
///
/// Сами сейвы весят 29 МБ и в репозиторий не входят. Путь к папке с ними
/// задаётся переменной окружения ERDTREE_KEEPER_FIXTURES; без неё тесты,
/// требующие файлов, пропускаются.
/// </summary>
public class SaveParsingTests
{
    private static string? FixtureDir =>
        Environment.GetEnvironmentVariable("ERDTREE_KEEPER_FIXTURES");

    private static string? FixturePath(string name)
    {
        var dir = FixtureDir;
        if (string.IsNullOrWhiteSpace(dir)) return null;
        var path = Path.Combine(dir, name);
        return File.Exists(path) ? path : null;
    }

    [Theory]
    // файл, персонаж, уровень, карта, локация, дистанция, босс, дистанция
    [InlineData("#1 Scadutree Fragment_before.sl2", "Grey-ka", 221, "m61_48_41_00",
        "Нижний поток реки Эллак", 162, "Золотой гиппопотам", 165)]
    [InlineData("#14 Scadutree Fragment x2_after.sl2", "Grey-ka", 221, "m61_47_46_00",
        "Теневой Альтус, запад", 255, "Дракон смерти", 150)]
    public async Task Reads_the_same_context_as_the_reference_parser(
        string file, string character, int level, string mapId,
        string location, int locationDistance, string boss, int bossDistance)
    {
        var path = FixturePath(file);
        Assert.SkipWhen(path is null, $"нет файла {file} - задайте ERDTREE_KEEPER_FIXTURES");

        var bytes = await Sl2File.ReadAllBytesSharedAsync(path!, TestContext.Current.CancellationToken);
        var context = SaveContextReader.Read(bytes);

        Assert.NotNull(context);
        Assert.Equal(character, context!.Character.Name);
        Assert.Equal(level, context.Character.Level);
        Assert.Equal(mapId, context.MapId);
        Assert.True(context.IsDlc);

        Assert.NotNull(context.Location);
        Assert.Equal(location, context.Location!.Ru);
        Assert.Equal(locationDistance, context.Location.Distance);

        Assert.NotNull(context.Boss);
        Assert.Equal(boss, context.Boss!.Ru);
        Assert.Equal(bossDistance, context.Boss.Distance);
    }

    [Fact]
    public async Task Healthy_save_passes_every_checksum()
    {
        var path = FixturePath("#1 Scadutree Fragment_before.sl2");
        Assert.SkipWhen(path is null, "нет файла - задайте ERDTREE_KEEPER_FIXTURES");

        var bytes = await Sl2File.ReadAllBytesSharedAsync(path!, TestContext.Current.CancellationToken);
        var report = Sl2File.CheckIntegrity(bytes);

        Assert.True(report.FileRecognised);
        Assert.True(report.SizeAsExpected);
        Assert.Equal(Sl2File.ChecksumBlockCount, report.Blocks.Count);
        Assert.True(report.AllOk, $"повреждённых блоков: {report.BadCount}");
    }

    [Fact]
    public async Task Damaged_byte_is_caught_by_the_checksum()
    {
        var path = FixturePath("#1 Scadutree Fragment_before.sl2");
        Assert.SkipWhen(path is null, "нет файла - задайте ERDTREE_KEEPER_FIXTURES");

        var bytes = await Sl2File.ReadAllBytesSharedAsync(path!, TestContext.Current.CancellationToken);
        // Портим один байт в данных первого слота - ровно так выглядит
        // повреждение, из-за которого игра пишет "Save data is corrupt".
        var at = Sl2File.SlotDataOffset(0) + 1024;
        bytes[at] ^= 0xff;

        var report = Sl2File.CheckIntegrity(bytes);

        Assert.False(report.AllOk);
        Assert.Equal(1, report.BadCount);
        Assert.False(report.Blocks[0].Ok);
    }

    [Fact]
    public void Rejects_a_file_that_is_not_a_save()
    {
        var junk = new byte[4096];
        Random.Shared.NextBytes(junk);

        var report = Sl2File.CheckIntegrity(junk);

        Assert.False(report.FileRecognised);
        Assert.False(report.AllOk);
    }

    [Fact]
    public void Decodes_map_ids_the_way_the_game_names_them()
    {
        var map = new MapId(Block: 0, TileZ: 41, TileX: 48, MapType: 61);

        Assert.Equal("m61_48_41_00", map.Id);
        Assert.True(map.IsOpenWorld);
        Assert.True(map.IsDlcMap);
    }

    [Fact]
    public void Map_point_reference_is_embedded_in_the_binary()
    {
        // Справочник должен ехать внутри exe: приложению нельзя зависеть ни
        // от сети, ни от соседних файлов.
        Assert.NotEmpty(MapPoints.Graces);
        Assert.NotEmpty(MapPoints.Bosses);
        Assert.Contains(MapPoints.Graces, p => !string.IsNullOrWhiteSpace(p.Ru));
    }
}

public class CharacterCardTests
{
    private static string? Fixture(string name)
    {
        var dir = Environment.GetEnvironmentVariable("ERDTREE_KEEPER_FIXTURES");
        if (string.IsNullOrWhiteSpace(dir)) return null;
        var path = Path.Combine(dir, name);
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public async Task Level_matches_the_stats_that_produced_it()
    {
        // Игра выводит уровень из вложенных очков: стартовый уровень плюс
        // разница между текущими и стартовыми характеристиками. Совпадение
        // означает, что восемь чисел прочитаны там, где надо, - при съехавшем
        // на четыре байта разборе сумма разошлась бы сразу.
        var path = Fixture("#1 Scadutree Fragment_before.sl2");
        Assert.SkipWhen(path is null, "нет файла - задайте ERDTREE_KEEPER_FIXTURES");

        var bytes = await Sl2File.ReadAllBytesSharedAsync(path!, TestContext.Current.CancellationToken);
        var slots = SaveParser.ReadSlots(bytes);

        Assert.NotEmpty(slots);
        foreach (var slot in slots)
        {
            Assert.Equal(slot.Level, slot.LevelFromStats);
        }
    }

    [Fact]
    public async Task Vitals_and_runes_look_like_a_real_character()
    {
        var path = Fixture("#1 Scadutree Fragment_before.sl2");
        Assert.SkipWhen(path is null, "нет файла - задайте ERDTREE_KEEPER_FIXTURES");

        var bytes = await Sl2File.ReadAllBytesSharedAsync(path!, TestContext.Current.CancellationToken);
        var slot = SaveParser.ReadSlots(bytes).OrderByDescending(s => s.Level).First();

        // Границы игры: характеристика от 1 до 99, здоровье и выносливость
        // положительные. Мусор из соседнего поля в них бы не уложился.
        foreach (var (name, value) in slot.Stats.All)
        {
            Assert.True(value is >= 1 and <= 99, $"{name} = {value} вне диапазона 1..99");
        }

        Assert.True(slot.MaxHp is > 0 and < 10000, $"здоровье {slot.MaxHp}");
        Assert.True(slot.MaxFp is > 0 and < 10000, $"мана {slot.MaxFp}");
        Assert.True(slot.MaxStamina is > 0 and < 10000, $"выносливость {slot.MaxStamina}");
        Assert.True(slot.Runes >= 0);
        Assert.True(slot.RuneMemory >= slot.Runes || slot.RuneMemory >= 0);
    }
}
