using System.Text;

namespace ErdtreeKeeper.Core;

/// <summary>Персонаж в слоте сохранения.</summary>
public sealed record CharacterSlot(
    int Index,
    string Name,
    int Level,
    int ClassId)
{
    public string ClassName => SaveParser.ClassNames.TryGetValue(ClassId, out var n) ? n : $"Класс {ClassId}";
}

/// <summary>Разобранный идентификатор карты.</summary>
public readonly record struct MapId(byte Block, byte TileZ, byte TileX, byte MapType)
{
    public string Id => $"m{MapType:D2}_{TileX:D2}_{TileZ:D2}_{Block:D2}";

    /// <summary>Открытый мир: базовая игра (60) и Земли Теней (61).</summary>
    public bool IsOpenWorld => MapType is 60 or 61;

    /// <summary>Карты DLC: открытый мир 61 и его подземелья 68/69.</summary>
    public bool IsDlcMap => MapType is 61 or 68 or 69;
}

/// <summary>Где именно стоит персонаж, в сырых координатах сейва.</summary>
public sealed record PlayerLocation(MapId Map, float X, float Y, float Z);

/// <summary>Данные, вынутые из одного слота.</summary>
public sealed record SlotDetails(
    PlayerLocation? Player,
    string? SteamId,
    int ScadutreeBlessing,
    int SpiritAshBlessing);

/// <summary>
/// Чтение .sl2 - имена персонажей, уровни, позиция и Steam ID.
///
/// Порт разборщика из проекта Erdtree Compass, проверенный на тех же файлах.
/// Слоты Elden Ring НЕ зашифрованы, поэтому всё сводится к последовательному
/// проходу по структуре: часть полей переменной длины, и до позиции игрока
/// иначе не добраться.
///
/// Всё чтение - только чтение. Ни один метод здесь ничего не пишет.
/// </summary>
public static class SaveParser
{
    public static readonly IReadOnlyDictionary<int, string> ClassNames = new Dictionary<int, string>
    {
        [0] = "Бродяга",
        [1] = "Воин",
        [2] = "Герой",
        [3] = "Разбойник",
        [4] = "Астролог",
        [5] = "Пророк",
        [6] = "Самурай",
        [7] = "Узник",
        [8] = "Исповедник",
        [9] = "Бедняга",
    };

    private const int GaItemCount = 0x1400;
    private const int CommonInventoryCount = 0xa80;
    private const int KeyInventoryCount = 0x180;
    private const int EventFlagsSize = 0x1bf99f;
    private const int PgdScadutreeOffset = 244;
    private const int PgdSpiritAshOffset = 245;
    private const int MaxLevel = 713;

    /// <summary>Перечисляет заполненные слоты персонажей.</summary>
    public static List<CharacterSlot> ReadSlots(byte[] file)
    {
        var slots = new List<CharacterSlot>();
        if (!Sl2File.HasBnd4Magic(file)) return slots;

        for (var i = 0; i < Sl2File.SlotCount; i++)
        {
            var slot = ReadSlotMetadata(file, i);
            if (slot is not null) slots.Add(slot);
        }

        return slots;
    }

    private static CharacterSlot? ReadSlotMetadata(byte[] file, int slotIndex)
    {
        var start = Sl2File.SlotDataOffset(slotIndex);
        if (start + Sl2File.SlotDataSize > file.LongLength) return null;

        try
        {
            var r = new Cursor(file, (int)start);

            if (r.U32() == 0) return null;      // ver: пустой слот

            r.Skip(4 + 0x18);                    // map_id + _0x18
            SkipGaItems(ref r);

            r.Skip(4 + 4);                       // _0x4, _0x4_1
            r.Skip(4 + 4 + 4);                   // health
            r.Skip(4 + 4 + 4);                   // fp
            r.Skip(4);
            r.Skip(4 + 4 + 4);                   // sp
            r.Skip(4);
            r.Skip(8 * 4);                       // восемь характеристик
            r.Skip(4 + 4 + 4);

            var level = (int)r.U32();
            if (level < 1 || level > MaxLevel) return null;

            r.Skip(4 + 4);                       // souls, soulsmemory
            r.Skip(0x28);

            var name = r.Utf16(16);
            if (string.IsNullOrEmpty(name)) return null;
            r.Skip(0x20);                        // character_name [u16; 16]

            r.Skip(0x2);
            r.Skip(1);                           // gender
            var classId = r.U8();

            return new CharacterSlot(slotIndex, name, level, classId);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Достаёт позицию игрока и Steam ID из слота.
    ///
    /// Приходится идти по всей структуре: инвентарь, снаряжение и флаги
    /// событий имеют переменный размер, и смещение координат заранее неизвестно.
    /// </summary>
    public static SlotDetails? ReadSlotDetails(byte[] file, int slotIndex)
    {
        var start = Sl2File.SlotDataOffset(slotIndex);
        if (start + Sl2File.SlotDataSize > file.LongLength) return null;

        try
        {
            var r = new Cursor(file, (int)start);

            r.Skip(4 + 4 + 0x18);                // ver + map_id + _0x18
            SkipGaItems(ref r);

            var pgdStart = r.Position;
            var scadutree = file[pgdStart + PgdScadutreeOffset];
            var spiritAsh = file[pgdStart + PgdSpiritAshOffset];
            r.Skip(432);                         // player_game_data

            r.Skip(0xd0);
            r.Skip(22 * 4);                      // equip_data
            r.Skip(29 * 4);                      // chr_asm
            r.Skip(22 * 4);                      // chr_asm2

            // equip_inventory_data: счётчики есть, но записи всегда занимают
            // весь массив - читаем фиксированную длину.
            if (r.U32() > CommonInventoryCount) return null;
            r.Skip(CommonInventoryCount * 12);
            if (r.U32() > KeyInventoryCount) return null;
            r.Skip(KeyInventoryCount * 12);
            r.Skip(4 + 4);

            r.Skip(0xc * 8 + 0x10 + 4);          // equip_magic_data
            r.Skip(0xa * 8 + 4 + 0x6 * 8 + 0x8); // equip_item_data
            r.Skip(6 * 4);                       // equip_gesture_data

            var projectiles = r.I32();           // переменная длина
            if (projectiles < 0 || projectiles > 10000) return null;
            r.Skip(projectiles * 8);

            r.Skip(39 * 4);                      // equipped_items
            r.Skip(2 * 4);                       // equip_physics_data
            r.Skip(4);
            r.Skip(0x12f);                       // face_data
            r.Skip(4 + 0x780 * 12 + 4 + 0x80 * 12 + 4 + 4); // storage_inventory_data
            r.Skip(0x40 * 4);                    // gesture_game_data

            var regions = r.U32();               // переменная длина
            if (regions > 5000) return null;
            r.Skip((int)regions * 4);

            r.Skip(40);                          // ride_game_data
            r.Skip(1 + 0x40 + 3 * 4);
            r.Skip(0x1008);                      // menu_profile_save_load
            r.Skip(0x34);                        // trophy_equip_data
            r.Skip(4 + 4 + 0x1b58 * 16);         // ga_item_data
            r.Skip(0x408);                       // tutorial_data
            r.Skip(0x1d);

            r.Skip(EventFlagsSize);              // флаги событий этому приложению не нужны
            r.Skip(1);

            for (var i = 0; i < 5; i++)          // пять блоков переменной длины
            {
                var length = r.I32();
                if (length < 0 || length > 0x100000) return null;
                r.Skip(length);
            }

            // Координаты идут первыми, следом - идентификатор карты.
            var mapRaw = r.PeekBytes(12, 4);
            var x = r.F32();
            var y = r.F32();
            var z = r.F32();
            r.Skip(4 + 0x11);                    // map_id + _0x11

            var empty = x == 0 && y == 0 && z == 0 &&
                        mapRaw[0] == 0 && mapRaw[1] == 0 && mapRaw[2] == 0 && mapRaw[3] == 0;
            var player = empty
                ? null
                : new PlayerLocation(new MapId(mapRaw[0], mapRaw[1], mapRaw[2], mapRaw[3]), x, y, z);

            r.Skip(12 + 0x10);                   // player_coords2
            r.Skip(0x0f + 4 + 0x20000);          // cs_net_data_chunks
            r.Skip(12 + 12 + 0x10);              // погода и время

            var low = r.U32();
            var high = r.U32();
            var steam = ((ulong)high << 32) | low;

            return new SlotDetails(
                player,
                steam == 0 ? null : steam.ToString(),
                Math.Min((int)scadutree, 20),
                Math.Min((int)spiritAsh, 10));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// ga_items: 0x1400 записей переменной длины. Размер зависит от типа
    /// предмета в старших битах id, поэтому пропустить блок целиком нельзя.
    /// </summary>
    private static void SkipGaItems(ref Cursor r)
    {
        for (var i = 0; i < GaItemCount; i++)
        {
            r.Skip(4);                           // gaitem_handle
            var itemId = r.U32();
            if (itemId == 0) continue;

            var kind = itemId & 0xf0000000;
            if (kind == 0) r.Skip(13);           // оружие
            else if (kind == 0x10000000) r.Skip(8); // броня
        }
    }

    /// <summary>Курсор по буферу с проверкой границ.</summary>
    private struct Cursor(byte[] data, int position)
    {
        private readonly byte[] _data = data;
        private int _pos = position;

        public readonly int Position => _pos;

        public void Skip(int count) => _pos += count;

        public byte U8() => _data[_pos++];

        public uint U32()
        {
            var v = BitConverter.ToUInt32(_data, _pos);
            _pos += 4;
            return v;
        }

        public int I32()
        {
            var v = BitConverter.ToInt32(_data, _pos);
            _pos += 4;
            return v;
        }

        public float F32()
        {
            var v = BitConverter.ToSingle(_data, _pos);
            _pos += 4;
            return v;
        }

        public readonly byte[] PeekBytes(int offset, int count) =>
            _data.AsSpan(_pos + offset, count).ToArray();

        /// <summary>Имя персонажа: UTF-16LE, обрывается нулём.</summary>
        public readonly string Utf16(int maxChars)
        {
            var sb = new StringBuilder(maxChars);
            for (var i = 0; i < maxChars; i++)
            {
                var at = _pos + i * 2;
                if (at + 1 >= _data.Length) break;
                var code = BitConverter.ToUInt16(_data, at);
                if (code == 0) break;
                sb.Append((char)code);
            }

            return sb.ToString();
        }
    }
}
