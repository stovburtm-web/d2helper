using Dota2GSI;
using Dota2GSI.Nodes;
using D2Helper.Core.Models;

namespace D2Helper.Core.Gsi;

/// <summary>
/// Трекер ворожих юнітів на minimap'і. На кожний GSI-tick (10Hz) поновлює last-seen позиції
/// видимих ворогів; невидимих тримає як «ghost» з ростом stale-таймера до ~15s.
///
/// Видає <see cref="EnemyPresenceSnapshot"/> який <see cref="DangerHeatmapRenderer"/>
/// підмішує у фінальний danger через V1.3-логіку (presence push + absence dampen).
///
/// Threading: НЕ thread-safe; викликати з одного UI-loop'а (як і renderer).
/// </summary>
public sealed class MinimapPresenceTracker
{
    private readonly Dictionary<string, (float Wx, float Wy, DateTime LastSeen)> _lastSeen = new();

    /// <summary>Скільки секунд тримаємо ghost-крапку без оновлень перед drop.</summary>
    public float MaxStaleSeconds { get; init; } = 15f;

    /// <summary>
    /// Будує снапшот ворожих героїв з поточного GameState.
    /// Сторона гравця — щоб знати яких юнітів вважати ворогами.
    /// </summary>
    /// <param name="gs">Поточний GSI стейт. Якщо <c>null</c> або немає minimap'а — повертає порожній snapshot.</param>
    /// <param name="myIsRadiant">true, якщо локальний гравець за Radiant (ворог = Dire).</param>
    /// <param name="now">Поточний час (для розрахунку stale).</param>
    public EnemyPresenceSnapshot Update(GameState? gs, bool myIsRadiant, DateTime now)
    {
        if (gs?.Minimap?.Elements is null)
            return new EnemyPresenceSnapshot(Array.Empty<EnemyDot>());

        var enemyTeam = myIsRadiant ? PlayerTeam.Dire : PlayerTeam.Radiant;
        // 1. Update last-seen для кожного видимого ворожого героя.
        foreach (var kvp in gs.Minimap.Elements)
        {
            var el = kvp.Value;
            if (el is null) continue;
            if (el.Team != enemyTeam) continue;
            var name = el.UnitName;
            if (string.IsNullOrEmpty(name)) continue;
            // Беремо тільки героїв — креіпи/ворди/курʼєр шумлять.
            if (!name.Contains("hero", StringComparison.OrdinalIgnoreCase)) continue;
            _lastSeen[name] = (el.Location.X, el.Location.Y, now);
        }

        // 2. Збираємо точки + чистимо застарілі.
        var dots = new List<EnemyDot>(_lastSeen.Count);
        var toRemove = new List<string>();
        foreach (var (name, info) in _lastSeen)
        {
            float stale = (float)(now - info.LastSeen).TotalSeconds;
            if (stale > MaxStaleSeconds)
            {
                toRemove.Add(name);
                continue;
            }
            dots.Add(new EnemyDot(info.Wx, info.Wy, stale));
        }
        foreach (var n in toRemove) _lastSeen.Remove(n);

        return new EnemyPresenceSnapshot(dots);
    }

    /// <summary>Скидає state (напр. при новому матчі).</summary>
    public void Reset() => _lastSeen.Clear();
}
