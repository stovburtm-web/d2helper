using System.Text.Json.Serialization;

namespace D2Helper.Core.Quests;

public sealed record ZoneDefinition
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("minX")] public double MinX { get; init; }
    [JsonPropertyName("maxX")] public double MaxX { get; init; }
    [JsonPropertyName("minY")] public double MinY { get; init; }
    [JsonPropertyName("maxY")] public double MaxY { get; init; }

    public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
}

public sealed record ZoneCatalogDefinition
{
    [JsonPropertyName("zones")] public List<ZoneDefinition> Zones { get; init; } = new();
}
