using System.Security.Cryptography;

namespace ErdtreeKeeper.Core;

/// <summary>
/// Раскладка файла сохранения Elden Ring (.sl2) и проверка его целостности.
///
/// Файл - контейнер BND4: заголовок 0x300, затем 10 слотов персонажей, затем
/// блок профиля. Каждый блок предваряют 16 байт MD5 от его данных - именно эту
/// сумму проверяет игра, и именно по ней видно, что файл побился.
///
/// Раскладка и алгоритм суммы проверены на реальных сейвах (все 11 сумм
/// сошлись байт в байт), а не взяты из описаний в интернете.
///
/// ВАЖНО: этот класс только читает. Приложение принципиально не меняет
/// содержимое сохранений - см. README, раздел "Что приложение делает".
/// </summary>
public static class Sl2File
{
    /// <summary>Сигнатура BND4 в начале файла.</summary>
    public static ReadOnlySpan<byte> Magic => "BND4"u8;

    public const int HeaderSize = 0x300;
    public const int SlotStride = 0x280010;
    public const int SlotDataSize = 0x280000;
    public const int SlotCount = 10;
    public const int ChecksumSize = 16;

    /// <summary>Смещение блока профиля: сразу за десятью слотами.</summary>
    public const long ProfileOffset = HeaderSize + (long)SlotCount * SlotStride;

    /// <summary>Размер данных профиля. Подобран и подтверждён на реальных файлах.</summary>
    public const int ProfileDataSize = 0x60000;

    /// <summary>Ожидаемый размер файла ванильной игры, байт.</summary>
    public const long VanillaSize = 28967888;

    /// <summary>Всего проверяемых блоков: 10 слотов + профиль.</summary>
    public const int ChecksumBlockCount = SlotCount + 1;

    /// <summary>Смещение данных слота (после его контрольной суммы).</summary>
    public static long SlotDataOffset(int slotIndex) =>
        HeaderSize + (long)slotIndex * SlotStride + ChecksumSize;

    /// <summary>Смещение контрольной суммы слота.</summary>
    public static long SlotChecksumOffset(int slotIndex) =>
        HeaderSize + (long)slotIndex * SlotStride;

    public static bool HasBnd4Magic(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[..4].SequenceEqual(Magic);

    /// <summary>
    /// Читает файл целиком, не мешая игре.
    ///
    /// FileShare.ReadWrite обязателен: игра держит сейв открытым, и без него
    /// чтение падало бы с "файл занят". Открываем только на чтение - записать
    /// в игровой файл через этот путь невозможно даже по ошибке.
    /// </summary>
    public static async Task<byte[]> ReadAllBytesSharedAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1 << 20,
            useAsync: true);

        var buffer = new byte[stream.Length];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0) break;
            read += n;
        }

        return read == buffer.Length ? buffer : buffer[..read];
    }

    /// <summary>Результат проверки одного блока.</summary>
    public readonly record struct BlockIntegrity(int Index, bool IsProfile, bool Ok, string Stored, string Actual)
    {
        public string Title => IsProfile ? Loc.Get("block.profile") : Loc.Get("block.slot", Index + 1);
    }

    /// <summary>Итог проверки целостности файла.</summary>
    public sealed record IntegrityReport(
        bool FileRecognised,
        bool SizeAsExpected,
        long ActualSize,
        IReadOnlyList<BlockIntegrity> Blocks)
    {
        public int BadCount => Blocks.Count(b => !b.Ok);

        /// <summary>
        /// Все ли блоки вообще нашлись.
        ///
        /// У обрезанного файла цикл прерывается и список остаётся пустым или
        /// неполным. Без этой проверки "повреждённых нет" означало бы "нечего
        /// было проверять", и файл нулевой длины считался бы целым.
        /// </summary>
        public bool Complete => Blocks.Count == ChecksumBlockCount;

        public bool AllOk => FileRecognised && Complete && BadCount == 0;

        /// <summary>Почему файл не годится. Пусто, если годится.</summary>
        public string? Problem
        {
            get
            {
                if (!FileRecognised) return Loc.Get("problem.notEldenRing");
                if (!Complete) return Loc.Get("problem.truncated", Blocks.Count, ChecksumBlockCount);
                if (BadCount > 0) return Loc.Get("problem.damaged", BadCount);
                return null;
            }
        }
    }

    /// <summary>
    /// Считает и сверяет все 11 контрольных сумм.
    ///
    /// Это единственный способ узнать, что сейв повреждён, ДО того как игрок
    /// восстановит его в игру и увидит "Save data is corrupt".
    /// </summary>
    public static IntegrityReport CheckIntegrity(byte[] data)
    {
        var blocks = new List<BlockIntegrity>(ChecksumBlockCount);

        if (!HasBnd4Magic(data))
        {
            return new IntegrityReport(false, false, data.LongLength, blocks);
        }

        for (var i = 0; i < SlotCount; i++)
        {
            var checksumAt = SlotChecksumOffset(i);
            var dataAt = SlotDataOffset(i);
            if (dataAt + SlotDataSize > data.LongLength) break;
            blocks.Add(Compare(data, (int)checksumAt, (int)dataAt, SlotDataSize, i, isProfile: false));
        }

        if (ProfileOffset + ChecksumSize + ProfileDataSize <= data.LongLength)
        {
            blocks.Add(Compare(
                data,
                (int)ProfileOffset,
                (int)(ProfileOffset + ChecksumSize),
                ProfileDataSize,
                SlotCount,
                isProfile: true));
        }

        return new IntegrityReport(true, data.LongLength == VanillaSize, data.LongLength, blocks);
    }

    private static BlockIntegrity Compare(byte[] data, int checksumAt, int dataAt, int length, int index, bool isProfile)
    {
        Span<byte> actual = stackalloc byte[ChecksumSize];
        MD5.HashData(data.AsSpan(dataAt, length), actual);

        var stored = data.AsSpan(checksumAt, ChecksumSize);
        var ok = stored.SequenceEqual(actual);

        return new BlockIntegrity(
            index,
            isProfile,
            ok,
            Convert.ToHexStringLower(stored),
            Convert.ToHexStringLower(actual));
    }
}
