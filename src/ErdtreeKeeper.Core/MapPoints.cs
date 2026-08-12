using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErdtreeKeeper.Core;

/// <summary>Точка на карте: место благодати или арена босса.</summary>
public sealed class MapPoint
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("ru")] public string Ru { get; set; } = "";
    [JsonPropertyName("mapType")] public int MapType { get; set; }
    [JsonPropertyName("mapId")] public string MapId { get; set; } = "";
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("z")] public double Z { get; set; }
    [JsonPropertyName("open")] public bool Open { get; set; }
    [JsonPropertyName("dlc")] public bool Dlc { get; set; }
}

public sealed class MapPointSet
{
    [JsonPropertyName("graces")] public List<MapPoint> Graces { get; set; } = [];
    [JsonPropertyName("bosses")] public List<MapPoint> Bosses { get; set; } = [];
}

/// <summary>Ближайшая точка и расстояние до неё в метрах.</summary>
public sealed record NearestPoint(string Name, string Ru, int Distance, bool Dlc);

[JsonSerializable(typeof(MapPointSet))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class MapPointsJsonContext : JsonSerializerContext;

/// <summary>
/// Справочник мест благодати и арен боссов с русскими названиями.
///
/// Лежит внутри исполняемого файла - приложению не нужны ни сеть, ни внешние
/// файлы, чтобы понять, где стоит персонаж.
/// </summary>
public static class MapPoints
{
    private static readonly Lazy<MapPointSet> Data = new(Load);

    public static IReadOnlyList<MapPoint> Graces => Data.Value.Graces;
    public static IReadOnlyList<MapPoint> Bosses => Data.Value.Bosses;

    private static MapPointSet Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("map-points.json", StringComparison.OrdinalIgnoreCase));
        if (name is null) return new MapPointSet();

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) return new MapPointSet();

        return JsonSerializer.Deserialize(stream, MapPointsJsonContext.Default.MapPointSet)
               ?? new MapPointSet();
    }

    /// <summary>
    /// Ищет ближайшую точку к позиции игрока.
    ///
    /// В открытом мире координаты складываются из номера клетки и смещения
    /// внутри неё: шаг клетки в сейве всегда 256. В подземельях координаты
    /// локальные, поэтому сравнивать можно только внутри той же карты - у
    /// соседних подземелий своя система отсчёта с нуля.
    /// </summary>
    public static NearestPoint? FindNearest(IReadOnlyList<MapPoint> points, PlayerLocation player)
    {
        var map = player.Map;
        var open = map.IsOpenWorld;

        var px = open ? map.TileX * 256.0 + player.X : player.X;
        var pz = open ? map.TileZ * 256.0 + player.Z : player.Z;

        MapPoint? best = null;
        var bestDistance = double.MaxValue;

        foreach (var p in points)
        {
            var matches = open
                ? p.Open && p.MapType == map.MapType
                : p.MapId == map.Id;
            if (!matches) continue;

            var dx = p.X - px;
            var dz = p.Z - pz;
            var distance = Math.Sqrt(dx * dx + dz * dz);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = p;
        }

        return best is null
            ? null
            : new NearestPoint(best.Name, best.Ru, (int)Math.Round(bestDistance), best.Dlc);
    }
}
