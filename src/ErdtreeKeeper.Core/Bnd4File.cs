using System.Buffers.Binary;
using System.Text;

namespace ErdtreeKeeper.Core;

/// <summary>
/// Контейнер BND4 - общая обёртка сохранений FromSoftware.
///
/// В него сложены сейвы Dark Souls II и III, Sekiro, Armored Core VI и
/// Nightreign. Elden Ring устроен так же, но про него известно больше, поэтому
/// у него собственная проверка в <see cref="Sl2File"/>.
///
/// Почему здесь проверяется только заголовок. Данные внутри этих игр
/// зашифрованы, ключ у игры, и пересчитать контрольные суммы, как в Elden Ring,
/// нельзя. Значит, честно проверить можно ровно две вещи: что файл вообще
/// BND4 и что он не обрезан - заголовки всех записей должны помещаться в его
/// длину. Обрыв копирования и подсунутый чужой файл этим ловятся, а вот
/// сказать "сейв цел" на этом основании нельзя, и программа так не говорит.
///
/// Раскладка заголовка (64 байта), проверена на реальных файлах:
///   0x00  "BND4"
///   0x0C  число записей
///   0x10  размер заголовка, всегда 0x40
///   0x18  версия, 8 байт текста
///   0x20  размер заголовка одной записи
/// </summary>
public static class Bnd4File
{
    public static ReadOnlySpan<byte> Magic => "BND4"u8;

    public const int HeaderSize = 0x40;

    /// <summary>Больше этого в сейве записей не бывает - число из заголовка испорченного файла отсекается.</summary>
    private const int SaneFileCount = 4096;

    /// <summary>Что удалось увидеть в заголовке.</summary>
    public sealed record Structure(
        bool Recognised,
        int FileCount,
        string Version,
        long DeclaredHeaderSize,
        long FileHeaderSize,
        bool HeadersFit,
        long ActualSize)
    {
        /// <summary>Файл похож на настоящий сейв: опознан, не обрезан, записи есть.</summary>
        public bool Ok => Recognised && HeadersFit && FileCount > 0;
    }

    public static bool HasMagic(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[..4].SequenceEqual(Magic);

    public static Structure Inspect(byte[] data)
    {
        if (!HasMagic(data) || data.LongLength < HeaderSize)
        {
            return new Structure(false, 0, "", 0, 0, false, data.LongLength);
        }

        var span = data.AsSpan();
        var fileCount = BinaryPrimitives.ReadInt32LittleEndian(span[0x0C..]);
        var declaredHeader = BinaryPrimitives.ReadInt64LittleEndian(span[0x10..]);
        var fileHeaderSize = BinaryPrimitives.ReadInt64LittleEndian(span[0x20..]);

        var version = Encoding.ASCII.GetString(span.Slice(0x18, 8)).TrimEnd('\0', ' ');

        var sane = fileCount is > 0 and <= SaneFileCount
                   && fileHeaderSize is > 0 and <= 0x400
                   && declaredHeader == HeaderSize;

        var headersFit = sane && HeaderSize + (long)fileCount * fileHeaderSize <= data.LongLength;

        return new Structure(true, fileCount, version, declaredHeader, fileHeaderSize, headersFit, data.LongLength);
    }
}
