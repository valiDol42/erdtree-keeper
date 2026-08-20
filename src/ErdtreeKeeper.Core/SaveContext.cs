namespace ErdtreeKeeper.Core;

/// <summary>
/// Что происходит в сейве: кто персонаж, где стоит и рядом с каким боссом.
/// Из этого собирается осмысленное имя снимка вместо ручного набора.
/// </summary>
public sealed record SaveContext(
    CharacterSlot Character,
    string MapId,
    bool IsDlc,
    NearestPoint? Location,
    NearestPoint? Boss,
    string? SteamId,
    int ScadutreeBlessing,
    int SpiritAshBlessing)
{
    public string Summary
    {
        get
        {
            var where = Location is null
                ? Loc.Get("ctx.nowhere")
                : Loc.Get("ctx.near", Location.Display, Location.Distance);
            var boss = Boss is null
                ? Loc.Get("ctx.noBoss")
                : Loc.Get("ctx.boss", Boss.Display, Boss.Distance);
            var dlc = IsDlc ? Loc.Get("ctx.dlc") + "  ·  " : "";
            return $"{dlc}{where}  ·  {boss}";
        }
    }
}

public static class SaveContextReader
{
    /// <summary>
    /// Разбирает сейв и описывает выбранный слот.
    ///
    /// Без явного номера берётся самый прокачанный персонаж: именно им играют,
    /// а слоты новичков обычно остались от старых экспериментов.
    /// </summary>
    public static SaveContext? Read(byte[] file, int? slotIndex = null)
    {
        var slots = SaveParser.ReadSlots(file);
        if (slots.Count == 0) return null;

        var slot = slotIndex is { } wanted
            ? slots.FirstOrDefault(s => s.Index == wanted)
            : slots.OrderByDescending(s => s.Level).First();
        if (slot is null) return null;

        var details = SaveParser.ReadSlotDetails(file, slot.Index);
        if (details?.Player is null)
        {
            return new SaveContext(slot, "", false, null, null, details?.SteamId, 0, 0);
        }

        var player = details.Player;
        var location = MapPoints.FindNearest(MapPoints.Graces, player);
        var boss = MapPoints.FindNearest(MapPoints.Bosses, player);

        // Земли Теней опознаём двумя путями: по типу карты и по ближайшей
        // точке. Подземелья DLC вроде катакомб делят номера карт с базовой
        // игрой, поэтому одного типа карты мало.
        var isDlc = player.Map.IsDlcMap || (location?.Dlc ?? false);

        return new SaveContext(
            slot,
            player.Map.Id,
            isDlc,
            location,
            boss,
            details.SteamId,
            details.ScadutreeBlessing,
            details.SpiritAshBlessing);
    }
}
