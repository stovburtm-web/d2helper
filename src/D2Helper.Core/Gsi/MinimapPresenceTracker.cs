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
        => UpdateForce(gs, myIsRadiant, now).Enemies;

    /// <summary>
    /// V1.4: будує одночасно <b>enemy</b> snapshot (з last-seen ghosting) та
    /// <b>ally</b> snapshot (із героїв + lane-крепів союзної команди, без ghosting).
    /// Союзні юніти подаються як <c>friendlyControl</c> у
    /// <see cref="DangerZoneModel.ComputeDangerDynamic"/> щоб counter-balance'ити загрозу:
    /// «союзники пушать мід з крепами → не червоне, бо є з ким приймати файт».
    /// </summary>
    public (EnemyPresenceSnapshot Enemies, EnemyPresenceSnapshot Allies) UpdateForce(
        GameState? gs, bool myIsRadiant, DateTime now)
    {
        if (gs?.Minimap?.Elements is null)
        {
            var empty = new EnemyPresenceSnapshot(Array.Empty<EnemyDot>());
            return (empty, empty);
        }

        var enemyTeam = myIsRadiant ? PlayerTeam.Dire : PlayerTeam.Radiant;
        var allyTeam = myIsRadiant ? PlayerTeam.Radiant : PlayerTeam.Dire;

        var allyDots = new List<EnemyDot>();

        // 1. Один прохід: оновлюємо ghost-state для ворогів + збираємо allies.
        foreach (var kvp in gs.Minimap.Elements)
        {
            var el = kvp.Value;
            if (el is null) continue;
            var name = el.UnitName;
            if (string.IsNullOrEmpty(name)) continue;

            bool isHero = name.Contains("hero", StringComparison.OrdinalIgnoreCase);
            bool isLaneCreep =
                name.Contains("creep", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("neutral", StringComparison.OrdinalIgnoreCase);

            if (el.Team == enemyTeam)
            {
                if (!isHero) continue; // ghost-трекаємо тільки ворожих героїв
                _lastSeen[name] = (el.Location.X, el.Location.Y, now);
            }
            else if (el.Team == allyTeam)
            {
                // Союзних видно завжди → stale=0; крепи мають меншу вагу (0.15).
                if (isHero)
                    allyDots.Add(new EnemyDot(el.Location.X, el.Location.Y, 0f, Weight: 1.0f));
                else if (isLaneCreep)
                    allyDots.Add(new EnemyDot(el.Location.X, el.Location.Y, 0f, Weight: 0.15f));
            }
        }

        // 2. Збираємо вороги + чистимо застарілі ghost'и.
        var enemyDots = new List<EnemyDot>(_lastSeen.Count);
        var toRemove = new List<string>();
        foreach (var (name, info) in _lastSeen)
        {
            float stale = (float)(now - info.LastSeen).TotalSeconds;
            if (stale > MaxStaleSeconds)
            {
                toRemove.Add(name);
                continue;
            }
            enemyDots.Add(new EnemyDot(info.Wx, info.Wy, stale));
        }
        foreach (var n in toRemove) _lastSeen.Remove(n);

        return (
            new EnemyPresenceSnapshot(enemyDots),
            new EnemyPresenceSnapshot(allyDots));
    }

    /// <summary>Скидає state (напр. при новому матчі).</summary>
    public void Reset() => _lastSeen.Clear();
}
