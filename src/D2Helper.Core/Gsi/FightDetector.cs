using Dota2GSI;
using Dota2GSI.Nodes;
using D2Helper.Core.Models;

namespace D2Helper.Core.Gsi;

/// <summary>
/// Детектор файтів за GSI-потоком. Тримає rolling buffer hp/mp по кожному союзнику
/// (~5 сек), і коли бачить різкий Δhp + поряд видно ворога — видає
/// <see cref="FightEvent"/>. Призначено щоб overlay блимнув пульсом на мінімапі
/// (а для Teamfight ще й пінгнув звуком), бо low-MMR гравці не дивляться на мінімапу.
///
/// Threading: не thread-safe; викликати з одного UI-loop'а.
///
/// Алгоритм коротко (V2.0):
/// <list type="bullet">
///   <item>Buffer на гравця — `Sample(time, hp%, mp%, alive, x, y)`, давність ≤ 5 сек.</item>
///   <item>Кожен tick рахуємо max(Δhp за 1с, Δhp за 2с) на кожного живого алі.</item>
///   <item>Поріг Skirmish: Δhp ≥ 12% за 2с **і** хоча б 1 ворог у 1500 unit'ів від алі.</item>
///   <item>Поріг Teamfight: Δhp ≥ 25%/2с або Δhp ≥ 18%/1с **АБО** 2+ алі під атакою **АБО**
///         3+ ворогів у 1500 unit'ів кластеризовано.</item>
///   <item>Death (alive: true→false) → форсує Teamfight.</item>
///   <item>Cooldown 4с (Skirmish) / 6с (Teamfight) на цей PlayerID щоб не спамити.</item>
/// </list>
///
/// Що НЕ тригерить (за дизайном):
/// <list type="bullet">
///   <item>Pudge Rot (~4%/с max-hp = 8%/2с) — нижче порогу Skirmish.</item>
///   <item>Armlet drain (~1.75%/с) — нижче порогу.</item>
///   <item>Будь-який повільний passive drain без ворогів у 1500 від алі.</item>
/// </list>
/// </summary>
public sealed class FightDetector
{
    private const int MaxSamplesPerAlly = 64; // ~6с при 10Hz
    private const float BufferSeconds = 5f;

    private const float SkirmishDeltaHp2s = 12f;    // 12% за 2с → Skirmish якщо є ворог поряд
    private const float TeamfightDeltaHp2s = 25f;   // 25% за 2с → Teamfight
    private const float TeamfightDeltaHp1s = 18f;   // 18% за 1с → Teamfight (burst)
    private const float EnemyProximityRadius = 1500f;
    private const int EnemyClusterMinCount = 3;     // 3+ ворогів кластеризовано → Teamfight

    private const float SkirmishCooldownSec = 4f;
    private const float TeamfightCooldownSec = 6f;

    private readonly struct Sample
    {
        public readonly DateTime Time;
        public readonly float HpPct;
        public readonly float MpPct;
        public readonly bool Alive;
        public readonly float Wx;
        public readonly float Wy;
        public Sample(DateTime t, float hp, float mp, bool alive, float wx, float wy)
        { Time = t; HpPct = hp; MpPct = mp; Alive = alive; Wx = wx; Wy = wy; }
    }

    private readonly Dictionary<int, LinkedList<Sample>> _buffers = new();
    private readonly Dictionary<int, DateTime> _cooldownUntil = new();
    private readonly Dictionary<int, bool> _lastAlive = new();

    /// <summary>
    /// Обробити новий GSI-snapshot і повернути події що виникли на цьому тіку.
    /// </summary>
    /// <param name="gs">GSI стейт. <c>null</c> або без team details → no-op.</param>
    /// <param name="myIsRadiant">Сторона локального гравця — щоб знати кого вважати алі.</param>
    /// <param name="enemies">Снепшот ворогів від <see cref="MinimapPresenceTracker"/> — для перевірки proximity.</param>
    /// <param name="now">Поточний час.</param>
    public IReadOnlyList<FightEvent> Update(
        GameState? gs, bool myIsRadiant, EnemyPresenceSnapshot? enemies, DateTime now)
    {
        if (gs is null) return Array.Empty<FightEvent>();
        var teamDetails = myIsRadiant ? gs.RadiantTeamDetails : gs.DireTeamDetails;
        if (teamDetails?.Players is null || teamDetails.Players.Count == 0)
            return Array.Empty<FightEvent>();

        // Адаптуємо GSI → нейтральний формат і дальше пайплайн без GSI-залежностей.
        var inputs = new List<AllyTickInput>(teamDetails.Players.Count);
        foreach (var kvp in teamDetails.Players)
        {
            var fp = kvp.Value;
            if (fp?.Hero is null) continue;
            inputs.Add(new AllyTickInput(
                PlayerId: fp.PlayerID,
                HpPct: fp.Hero.HealthPercent,
                MpPct: fp.Hero.ManaPercent,
                IsAlive: fp.Hero.IsAlive,
                Wx: fp.Hero.Location.X,
                Wy: fp.Hero.Location.Y));
        }
        return Update(inputs, enemies, now);
    }

    /// <summary>
    /// Чистий тестабельний overload: приймає вже видобуті семпли алі (без GSI-залежності).
    /// Виробничий путь <see cref="Update(GameState, bool, EnemyPresenceSnapshot, DateTime)"/>
    /// просто адаптує GSI → <see cref="AllyTickInput"/> і викликає цей метод.
    /// </summary>
    public IReadOnlyList<FightEvent> Update(
        IReadOnlyList<AllyTickInput> allies, EnemyPresenceSnapshot? enemies, DateTime now)
    {
        if (allies is null || allies.Count == 0) return Array.Empty<FightEvent>();

        var events = new List<FightEvent>();
        // Тимчасові ділянки: алі чий Δhp перетнув Skirmish-поріг ЦЬОГО тіку → ниже
        // зведемо в кластер для Teamfight-ескалації "2+ алі під атакою".
        var allyUnderAttack = new List<(int PlayerId, float Wx, float Wy, float DeltaHp2s, float DeltaHp1s, int NearbyEnemies)>();

        foreach (var ally in allies)
        {
            int playerId = ally.PlayerId;
            float wx = ally.Wx;
            float wy = ally.Wy;
            float hpPct = ally.HpPct;
            float mpPct = ally.MpPct;
            bool isAlive = ally.IsAlive;

            // 1. Death edge — порівняння з попереднім станом. Робимо до додавання семплу.
            bool wasAlive = _lastAlive.TryGetValue(playerId, out var pa) && pa;
            _lastAlive[playerId] = isAlive;
            if (wasAlive && !isAlive)
            {
                // Беремо останню відому позицію живого алі (бо при смерті координати можуть скинутись).
                var lastLivePos = GetLastLivePosition(playerId, fallbackX: wx, fallbackY: wy);
                if (CooldownAllows(playerId, now))
                {
                    events.Add(new FightEvent(now, lastLivePos.x, lastLivePos.y,
                        FightSeverity.Teamfight,
                        new[] { playerId },
                        "death"));
                    _cooldownUntil[playerId] = now.AddSeconds(TeamfightCooldownSec);
                }
                // Очищаємо буфер мертвого героя — нові семпли почнемо після респу.
                _buffers.Remove(playerId);
                continue;
            }
            if (!isAlive) continue;

            // 2. Поновлюємо буфер + чистимо застарілі семпли.
            if (!_buffers.TryGetValue(playerId, out var buf))
            {
                buf = new LinkedList<Sample>();
                _buffers[playerId] = buf;
            }
            buf.AddLast(new Sample(now, hpPct, mpPct, isAlive, wx, wy));
            while (buf.Count > MaxSamplesPerAlly) buf.RemoveFirst();
            while (buf.First is not null && (now - buf.First.Value.Time).TotalSeconds > BufferSeconds)
                buf.RemoveFirst();

            // 3. Δhp за вікнами 1с і 2с — старий-семпл-в-вікні мінус поточний.
            float dhp1 = ComputeDeltaHpOverWindow(buf, now, 1.0f);
            float dhp2 = ComputeDeltaHpOverWindow(buf, now, 2.0f);
            float dhp = MathF.Max(dhp1, dhp2);
            if (dhp < SkirmishDeltaHp2s) continue;

            // 4. Перевірка proximity ворогів — фільтр від self-damage/passive drain.
            int nearby = CountEnemiesNear(enemies, wx, wy, EnemyProximityRadius);
            if (nearby < 1) continue; // без ворога поряд = швидше за все це не файт

            allyUnderAttack.Add((playerId, wx, wy, dhp2, dhp1, nearby));
        }

        // 5. Класифікація severity з урахуванням кластеризації.
        // Якщо 2+ алі під атакою одночасно → ВСІ підіймаються до Teamfight.
        bool clusteredAllies = allyUnderAttack.Count >= 2;
        foreach (var a in allyUnderAttack)
        {
            if (!CooldownAllows(a.PlayerId, now)) continue;

            FightSeverity sev;
            string reason;
            if (a.DeltaHp2s >= TeamfightDeltaHp2s)
            { sev = FightSeverity.Teamfight; reason = "burst2s"; }
            else if (a.DeltaHp1s >= TeamfightDeltaHp1s)
            { sev = FightSeverity.Teamfight; reason = "burst1s"; }
            else if (clusteredAllies)
            { sev = FightSeverity.Teamfight; reason = "cluster_allies"; }
            else if (a.NearbyEnemies >= EnemyClusterMinCount)
            { sev = FightSeverity.Teamfight; reason = "cluster_enemies"; }
            else
            { sev = FightSeverity.Skirmish; reason = "skirmish"; }

            var ids = clusteredAllies
                ? allyUnderAttack.Select(x => x.PlayerId).ToArray()
                : new[] { a.PlayerId };
            events.Add(new FightEvent(now, a.Wx, a.Wy, sev, ids, reason));
            _cooldownUntil[a.PlayerId] = now.AddSeconds(
                sev == FightSeverity.Teamfight ? TeamfightCooldownSec : SkirmishCooldownSec);

            // Якщо вже видали кластерну подію — дублювати на інших алі немає сенсу.
            if (clusteredAllies) break;
        }

        return events;
    }

    /// <summary>Чи минув cooldown для цього алі (щоб не спамити пульсами на одну й ту ж бійку).</summary>
    private bool CooldownAllows(int playerId, DateTime now)
        => !_cooldownUntil.TryGetValue(playerId, out var until) || now >= until;

    /// <summary>Δhp = oldest-in-window − current. Якщо мало семплів — повертає 0.</summary>
    private static float ComputeDeltaHpOverWindow(LinkedList<Sample> buf, DateTime now, float windowSec)
    {
        if (buf.Count < 2) return 0f;
        var current = buf.Last!.Value;
        // Знаходимо найстаріший семпл у вікні.
        Sample? oldestInWindow = null;
        foreach (var s in buf)
        {
            if ((now - s.Time).TotalSeconds <= windowSec)
            {
                oldestInWindow = s;
                break;
            }
        }
        if (oldestInWindow is null) return 0f;
        return oldestInWindow.Value.HpPct - current.HpPct;
    }

    /// <summary>Скільки ворожих heroes/creeps з ваг ≥0.5 в межах <paramref name="radius"/> від точки.</summary>
    private static int CountEnemiesNear(EnemyPresenceSnapshot? enemies, float wx, float wy, float radius)
    {
        if (enemies is null) return 0;
        int count = 0;
        float r2 = radius * radius;
        foreach (var d in enemies.Dots)
        {
            // Враховуємо тільки "живі" та свіжі (stale <3с) ворогі-крапки. Ghost-юніт за 5+с
            // — вже застаріла інформація, не доказ що він точно тут.
            if (d.StaleSeconds > 3f) continue;
            if (d.IsPassive) continue;
            if (d.Weight < 0.5f) continue;
            float dx = d.Wx - wx;
            float dy = d.Wy - wy;
            if (dx * dx + dy * dy <= r2) count++;
        }
        return count;
    }

    private (float x, float y) GetLastLivePosition(int playerId, float fallbackX, float fallbackY)
    {
        if (!_buffers.TryGetValue(playerId, out var buf) || buf.Last is null)
            return (fallbackX, fallbackY);
        return (buf.Last.Value.Wx, buf.Last.Value.Wy);
    }
}

/// <summary>
/// Нейтральний DTO семплу одного союзника на одному тіку. Не залежить від Dota2GSI —
/// можна заповнювати як з реального GSI (виробництво) так і вручну (юніт-тести).
/// </summary>
public readonly record struct AllyTickInput(
    int PlayerId,
    float HpPct,
    float MpPct,
    bool IsAlive,
    float Wx,
    float Wy);
