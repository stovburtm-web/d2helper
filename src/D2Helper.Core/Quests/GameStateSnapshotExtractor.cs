using System.Globalization;
using Dota2GSI;

namespace D2Helper.Core.Quests;

internal static class GameStateSnapshotExtractor
{
    public static GameStateSnapshot Extract(GameState gs)
    {
        var root = (object)gs;
        var px = TryFindDouble(root, "xpos", "x", "posx", "positionx");
        var py = TryFindDouble(root, "ypos", "y", "posy", "positiony");

        return new GameStateSnapshot
        {
            GoldSpent = TryFindInt(root, "goldspent", "gold_spent") ?? 0,
            Denies = TryFindInt(root, "denies") ?? 0,
            WardsPlaced = TryFindInt(root, "wardsplaced", "wards_placed") ?? 0,
            PositionX = px,
            PositionY = py,
        };
    }

    private static int? TryFindInt(object root, params string[] names)
    {
        var d = TryFindDouble(root, names);
        return d is null ? null : (int)Math.Round(d.Value);
    }

    private static double? TryFindDouble(object root, params string[] names)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var q = new Queue<(object Obj, int Depth)>();
        q.Enqueue((root, 0));

        while (q.Count > 0)
        {
            var (obj, depth) = q.Dequeue();
            if (!visited.Add(obj)) continue;
            if (depth > 4) continue;

            var type = obj.GetType();
            foreach (var prop in type.GetProperties())
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                object? value;
                try { value = prop.GetValue(obj); } catch { continue; }
                if (value is null) continue;

                var normalized = Normalize(prop.Name);
                if (names.Any(n => Normalize(n) == normalized))
                {
                    if (TryConvert(value, out var parsed)) return parsed;
                }

                if (value is string) continue;
                if (!prop.PropertyType.IsClass || prop.PropertyType == typeof(decimal)) continue;
                q.Enqueue((value, depth + 1));
            }
        }

        return null;
    }

    private static bool TryConvert(object value, out double parsed)
    {
        switch (value)
        {
            case int i:
                parsed = i;
                return true;
            case long l:
                parsed = l;
                return true;
            case float f:
                parsed = f;
                return true;
            case double d:
                parsed = d;
                return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v):
                parsed = v;
                return true;
            default:
                parsed = 0;
                return false;
        }
    }

    private static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
