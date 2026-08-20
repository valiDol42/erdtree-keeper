using System.Security.Cryptography;
using Xunit;

namespace ErdtreeKeeper.Core.Tests;

/// <summary>
/// Проверка трёх обещаний из README, которые до этого держались только на
/// порядке строк в коде: резервная копия делается ДО перезаписи, негодный
/// снимок в игру не попадает, ручные копии ротация не трогает.
///
/// Настоящие сейвы весят 29 МБ и в репозиторий не входят, поэтому здесь
/// собирается корректный файл нужной раскладки прямо в памяти.
/// </summary>
public class SnapshotServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "erdtree-keeper-tests", Guid.NewGuid().ToString("N"));

    private readonly SnapshotService _service = new(new ActivityLog());

    public SnapshotServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>Собирает файл, который пройдёт проверку целостности.</summary>
    private static byte[] BuildValidSave(byte fill = 0x11)
    {
        var data = new byte[Sl2File.VanillaSize];
        Sl2File.Magic.CopyTo(data);

        for (var i = 0; i < Sl2File.SlotCount; i++)
        {
            var at = (int)Sl2File.SlotDataOffset(i);
            data[at] = fill;
            MD5.HashData(data.AsSpan(at, Sl2File.SlotDataSize))
                .CopyTo(data, (int)Sl2File.SlotChecksumOffset(i));
        }

        var profileAt = (int)Sl2File.ProfileOffset + Sl2File.ChecksumSize;
        data[profileAt] = fill;
        MD5.HashData(data.AsSpan(profileAt, Sl2File.ProfileDataSize))
            .CopyTo(data, (int)Sl2File.ProfileOffset);

        return data;
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    // ─── Восстановление ─────────────────────────────────────────────────

    [Fact]
    public async Task Backs_the_current_save_up_before_overwriting_it()
    {
        var game = Write("game/ER0000.sl2", BuildValidSave(0xAA));
        var snapshot = Write("snapshots/Годрик.sl2", BuildValidSave(0xBB));
        var before = await File.ReadAllBytesAsync(game, TestContext.Current.CancellationToken);

        var result = await _service.RestoreAsync(snapshotPath: snapshot, gameSavePath: game);

        Assert.True(result.Success, result.Message);

        var backups = Directory.GetFiles(
            Path.Combine(_root, "game", SnapshotService.RestoreBackupFolder));
        Assert.Single(backups);

        // Копия должна быть именно прежним сейвом, а не тем, что мы восстановили.
        var saved = await File.ReadAllBytesAsync(backups[0], TestContext.Current.CancellationToken);
        Assert.Equal(SHA256.HashData(before), SHA256.HashData(saved));

        // А в игре теперь снимок.
        var now = await File.ReadAllBytesAsync(game, TestContext.Current.CancellationToken);
        Assert.Equal(
            SHA256.HashData(await File.ReadAllBytesAsync(snapshot, TestContext.Current.CancellationToken)),
            SHA256.HashData(now));
    }

    [Theory]
    [InlineData(0, "пустой файл")]
    [InlineData(1024, "обрезок в килобайт")]
    [InlineData(3_000_000, "обрезок в три мегабайта")]
    public async Task Refuses_a_truncated_snapshot_and_leaves_the_game_save_alone(int size, string what)
    {
        var game = Write("game/ER0000.sl2", BuildValidSave(0xAA));
        var untouched = await File.ReadAllBytesAsync(game, TestContext.Current.CancellationToken);

        // Обрезок настоящего сейва: подпись BND4 на месте, суммы первых блоков
        // даже сойдутся - и раньше такой файл проходил как "целый".
        var broken = BuildValidSave(0xCC)[..size];
        var snapshot = Write("snapshots/обрезок.sl2", broken);

        var result = await _service.RestoreAsync(snapshotPath: snapshot, gameSavePath: game);

        Assert.False(result.Success, $"{what} прошёл восстановление");
        Assert.Contains("не годится", result.Message);

        var after = await File.ReadAllBytesAsync(game, TestContext.Current.CancellationToken);
        Assert.Equal(SHA256.HashData(untouched), SHA256.HashData(after));
    }

    [Fact]
    public async Task Refuses_a_file_that_is_not_a_save_at_all()
    {
        var game = Write("game/ER0000.sl2", BuildValidSave(0xAA));
        var untouched = await File.ReadAllBytesAsync(game, TestContext.Current.CancellationToken);
        var snapshot = Write("snapshots/чужой.sl2", "это просто текст"u8.ToArray());

        var result = await _service.RestoreAsync(snapshotPath: snapshot, gameSavePath: game);

        Assert.False(result.Success);
        Assert.Equal(
            SHA256.HashData(untouched),
            SHA256.HashData(await File.ReadAllBytesAsync(game, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Damaged_block_stops_the_restore()
    {
        var game = Write("game/ER0000.sl2", BuildValidSave(0xAA));
        var broken = BuildValidSave(0xBB);
        broken[(int)Sl2File.SlotDataOffset(0) + 512] ^= 0xff;   // сумма первого слота больше не сходится
        var snapshot = Write("snapshots/битый.sl2", broken);

        var result = await _service.RestoreAsync(snapshotPath: snapshot, gameSavePath: game);

        Assert.False(result.Success);
        Assert.Contains("повреждённых блоков", result.Message);
    }

    // ─── Ротация ────────────────────────────────────────────────────────

    [Fact]
    public void Rotation_deletes_only_its_own_autosaves()
    {
        var folder = Path.Combine(_root, "autosave");
        Directory.CreateDirectory(folder);

        // Свои: имя заканчивается меткой времени.
        var mine = new[]
        {
            "Лимгрейв_2026-08-01_10-00-00.sl2",
            "Лимгрейв_2026-08-02_10-00-00.sl2",
            "Лимгрейв_2026-08-03_10-00-00.sl2",
        };

        // Чужие: ручная копия, живой сейв игры и его резервная копия.
        var others = new[] { "Годрик_before.sl2", "ER0000.sl2", "ER0000.sl2.bak" };

        foreach (var name in mine.Concat(others))
        {
            File.WriteAllBytes(Path.Combine(folder, name), [1, 2, 3]);
        }

        var removed = _service.Rotate(folder, keep: 1);

        Assert.Equal(2, removed);
        foreach (var name in others)
        {
            Assert.True(File.Exists(Path.Combine(folder, name)), $"{name} удалён, хотя не наш");
        }

        Assert.Single(Directory.GetFiles(folder).Where(f => SnapshotNaming.IsAutoName(Path.GetFileName(f))));
    }

    [Fact]
    public void Rotation_with_a_nonsense_limit_deletes_nothing()
    {
        var folder = Path.Combine(_root, "autosave-zero");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Лимгрейв_2026-08-01_10-00-00.sl2"), [1]);

        Assert.Equal(0, _service.Rotate(folder, keep: 0));
        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public void Recognises_its_own_autosave_names()
    {
        Assert.True(SnapshotNaming.IsAutoName("Лимгрейв_2026-08-12_10-31-02.sl2"));
        Assert.True(SnapshotNaming.IsAutoName("DLC_Теневой Альтус_2026-08-12_10-31-02.co2"));

        Assert.False(SnapshotNaming.IsAutoName("ER0000.sl2"));
        Assert.False(SnapshotNaming.IsAutoName("Годрик_before.sl2"));
        Assert.False(SnapshotNaming.IsAutoName("ER0000.sl2.bak"));
    }
}

public class GameFolderGuardTests
{
    [Theory]
    [InlineData(@"C:\game\EldenRing", @"C:\game\EldenRing", true)]
    [InlineData(@"C:\game\EldenRing", @"C:\game\EldenRing\76561190000000001", true)]
    [InlineData(@"C:\game\EldenRing", @"C:\game\eldenring\SUB", true)]
    [InlineData(@"C:\game\EldenRing", @"C:\game\EldenRingOther", false)]
    [InlineData(@"C:\game\EldenRing", @"D:\Снимки", false)]
    public void Recognises_paths_inside_the_game_folder(string root, string path, bool inside)
    {
        // Сюда нельзя складывать снимки: ротация удаляла бы файлы игры, а
        // снимок с именем ER0000 перезаписал бы живой сейв без бэкапа.
        Assert.Equal(inside, GameSaves.IsInsideGameFolder(path, root));
    }

    [Fact]
    public void Empty_path_is_not_inside_anything()
    {
        Assert.False(GameSaves.IsInsideGameFolder(null));
        Assert.False(GameSaves.IsInsideGameFolder("   "));
    }
}
