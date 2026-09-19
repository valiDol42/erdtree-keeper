using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace ErdtreeKeeper.Core.Tests;

/// <summary>
/// Глубина проверки зависит от игры, и это обещание, а не деталь. Здесь
/// проверяется, что программа не приписывает себе лишнего: там, где она видит
/// только структуру файла, она не должна отвечать "сохранение цело".
/// </summary>
public class SaveFormatTests
{
    public SaveFormatTests() => Loc.Current.Language = Lang.Ru;

    private static GameProfile DarkSouls3 => GameProfiles.BuiltIn.First(g => g.Id == "dark-souls-3");

    private static GameProfile Unknown => GameProfiles.FromCustom(new CustomGame
    {
        Id = "custom-test",
        Name = "Неизвестная игра",
        Folder = Path.GetTempPath(),
    });

    [Fact]
    public void A_real_looking_container_is_recognised()
    {
        var structure = Bnd4File.Inspect(Container(entries: 12, entryHeader: 0x20, extra: 12 * 0x20 + 256));

        Assert.True(structure.Recognised);
        Assert.True(structure.HeadersFit);
        Assert.Equal(12, structure.FileCount);
        Assert.Equal("BND4", structure.Version[..4]);
        Assert.True(structure.Ok);
    }

    /// <summary>
    /// Обрезанный файл - самый вероятный вид поломки: копирование прервалось,
    /// диск кончился. Записи не помещаются в длину файла, и это видно.
    /// </summary>
    [Fact]
    public void A_truncated_container_is_caught()
    {
        var structure = Bnd4File.Inspect(Container(entries: 500, entryHeader: 0x20, extra: 64));

        Assert.True(structure.Recognised);
        Assert.False(structure.HeadersFit);
        Assert.False(structure.Ok);
    }

    [Fact]
    public void A_foreign_file_is_not_a_save()
    {
        var structure = Bnd4File.Inspect(Encoding.ASCII.GetBytes("это просто текст, и довольно длинный"));

        Assert.False(structure.Recognised);
        Assert.False(structure.Ok);
    }

    [Fact]
    public void Dark_souls_checks_the_container_and_says_so()
    {
        var check = SaveIntegrity.Inspect(DarkSouls3, Container(entries: 12, entryHeader: 0x20, extra: 4096));

        Assert.True(check.Ok);
        Assert.Equal(SaveLayout.Bnd4, check.Layout);
        Assert.Null(check.EldenRing);

        var report = SaveIntegrity.BuildReport(DarkSouls3, check, "DS30000.sl2");

        Assert.Contains("Dark Souls III", report);
        Assert.Contains("Записей внутри: 12", report);

        // Программа обязана сказать, чего эта проверка НЕ покрывает.
        Assert.Contains("зашифрованы", report);
        Assert.Contains("не ловятся", report);
        Assert.DoesNotContain("игра его загрузит", report);
    }

    [Fact]
    public void A_truncated_dark_souls_save_is_refused()
    {
        var check = SaveIntegrity.Inspect(DarkSouls3, Container(entries: 500, entryHeader: 0x20, extra: 16));

        Assert.False(check.Ok);
        Assert.NotNull(check.Problem);
        Assert.Contains("обрезан", check.Problem);
    }

    /// <summary>
    /// Для незнакомой игры единственное честное утверждение - что копия точна.
    /// Отказывать в восстановлении собственного файла из-за незнания формата
    /// программа не должна.
    /// </summary>
    [Fact]
    public void An_unknown_format_is_allowed_but_not_praised()
    {
        var check = SaveIntegrity.Inspect(Unknown, [1, 2, 3, 4, 5]);

        Assert.True(check.Ok);
        Assert.Equal(SaveLayout.Opaque, check.Layout);

        var report = SaveIntegrity.BuildReport(Unknown, check, "slot1.sav");

        Assert.Contains("неизвестен", report);
        Assert.Contains("SHA-256", report);
        Assert.DoesNotContain("контрольных сумм", report);
    }

    /// <summary>Пустой файл не проходит нигде: его восстановление стёрло бы живой сейв.</summary>
    [Theory]
    [InlineData(SaveLayout.EldenRing)]
    [InlineData(SaveLayout.Bnd4)]
    [InlineData(SaveLayout.Opaque)]
    public void An_empty_file_is_refused_for_every_game(SaveLayout layout)
    {
        var game = layout switch
        {
            SaveLayout.EldenRing => GameProfiles.EldenRing,
            SaveLayout.Bnd4 => DarkSouls3,
            _ => Unknown,
        };

        var check = SaveIntegrity.Inspect(game, []);

        Assert.False(check.Ok);
        Assert.Equal(Loc.Get("problem.empty"), check.Problem);
    }

    /// <summary>
    /// Сейв Elden Ring, подсунутый как сейв Dark Souls, структуру пройдёт: это
    /// тот же контейнер. Зато настоящая проверка Elden Ring отличит их сразу -
    /// и именно она применяется к Elden Ring.
    /// </summary>
    [Fact]
    public void Elden_ring_still_gets_the_full_check()
    {
        var check = SaveIntegrity.Inspect(GameProfiles.EldenRing, Container(entries: 12, entryHeader: 0x20, extra: 4096));

        Assert.False(check.Ok);
        Assert.NotNull(check.EldenRing);
        Assert.False(check.EldenRing.Complete);
    }

    /// <summary>Заголовок BND4 в том виде, в каком его пишут игры.</summary>
    private static byte[] Container(int entries, int entryHeader, int extra)
    {
        var data = new byte[Bnd4File.HeaderSize + Math.Max(0, extra)];
        "BND4"u8.CopyTo(data);

        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), entries);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(0x10), Bnd4File.HeaderSize);
        Encoding.ASCII.GetBytes("BND4OK  ").CopyTo(data, 0x18);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(0x20), entryHeader);

        return data;
    }
}
