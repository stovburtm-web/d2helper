using System.Text.Json;

namespace D2Helper.Core.Quests;

public sealed class ZoneCatalog
{
    private readonly Dictionary<string, ZoneDefinition> _zones;

    public ZoneCatalog(IEnumerable<ZoneDefinition> zones)
    {
        _zones = zones.ToDictionary(z => z.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static ZoneCatalog LoadDefault()
    {
        var path = DataFileLocator.FindFromAppBase(Path.Combine("data", "zones.json"));
        if (path is null) throw new FileNotFoundException("zones.json not found");
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<ZoneCatalogDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return new ZoneCatalog(doc?.Zones ?? new List<ZoneDefinition>());
    }

    public bool IsInside(string? zoneId, double x, double y)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) return false;
        return _zones.TryGetValue(zoneId, out var zone) && zone.Contains(x, y);
    }
}
