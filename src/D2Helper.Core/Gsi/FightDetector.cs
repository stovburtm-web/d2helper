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
/// V2.1 — превентивні сигнали (без чекання burst HP):
/// <list type="bullet">
///   <item><b>CC edge:</b> алі перейшов <c>None → Stunned|Hexed|Silenced|Disarmed</c> і ≥1 ворог
///         у 1500 → миттєвий Skirmish reason="cc_*". Якщо ≥2 алі CCed одночасно — Teamfight
///         reason="cluster_cc". Це врятувало б 0-dmg-death (Naga sleep / ES totem combo).</item>
///   <item><b>Enemy cluster prefight:</b> 3+ ворогів у 1500 від алі ТА ВСЕ ЩЕ нема burst'у —
///         Skirmish reason="prefight_cluster" з cooldown 8с. Дає warning ДО першого удару.</item>
///   <item><b>Isolation:</b> ≥1 ворог у 1500 ТА найближчий інший живий алі &gt;2000 → Skirmish
///         reason="isolated". Класичний pickoff-prelude (саппорт відірваний від групи).</item>
/// </list>
///
/// Root/Slow з GSI <b>не</b> видно (Veno gel, CM frostbite, Treant overgrowth, Slark pounce
/// тощо не виставляють жодних прапорів) — їх ловимо тільки опосередковано через HP-drop.
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
    // V2.1: окремі cooldown'и для превентивних сигналів — щоб не змішувались з burst-cooldown.
    private const float PrefightCooldownSec = 8f;  // enemy_cluster_prefight + isolated
    private const float CcCooldownSec = 5f;        // cc_* edges

    // V2.2: пороги для нових правил.
    private const float IsolationMinAllyDistance = 2000f; // далі цього — алі вважається ізольованим
    private const int   IsolationMinEnemyCount   = 1;     // ≥ скільки ворогів поряд для тригера

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
    // V2.1/V2.2: окремі cooldown'и для CC-edge та prefight-сигналів, щоб burst-cooldown
    // не глушив принципово інший за природою warning.
    private readonly Dictionary<int, CcFlags> _lastCc = new();
    private readonly Dictionary<int, DateTime> _ccCooldownUntil = new();
    private readonly Dictionary<int, DateTime> _prefightCooldownUntil = new();

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
                Wy: fp.Hero.Location.Y,
                Cc: ExtractCc(fp.Hero)));
        }
        return Update(inputs, enemies, now);
    }

    /// <summary>
    /// V2.1: Витягуємо bit-маску CC з GSI <c>HeroDetails</c>. Beauty-properties
    /// <c>IsStunned/IsSilenced/IsDisarmed/IsHexed</c> уже декодують <c>HeroState</c> прапори
    /// — користуємось ними напряму. Root/Slow GSI не віддає (повертаємо без них).
    /// </summary>
    private static CcFlags ExtractCc(Dota2GSI.Nodes.HeroProvider.HeroDetails hero)
    {
        var cc = CcFlags.None;
        if (hero.IsStunned)  cc |= CcFlags.Stunned;
        if (hero.IsHexed)    cc |= CcFlags.Hexed;
        if (hero.IsSilenced) cc |= CcFlags.Silenced;
        if (hero.IsDisarmed) cc |= CcFlags.Disarmed;
        return cc;
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
        // V2.1: алі що *тільки-но* увійшов у CC цього тіку (edge None→Cc). Бітмаска — які саме нові
        // прапори додались (щоб у Reason можна було назвати "cc_stunned" чи "cc_hexed").
        var ccTriggered = new List<(int PlayerId, float Wx, float Wy, CcFlags NewBits, int NearbyEnemies)>();
        // V2.2: prefight-сигнали які НЕ потребують burst HP. Cooldown окремий від burst.
        var prefightEvents = new List<(int PlayerId, float Wx, float Wy, string Reason)>();

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
                // Очищаємо буфери мертвого героя — нові семпли почнемо після респу.
                _buffers.Remove(playerId);
                _lastCc.Remove(playerId);
                continue;
            }
            if (!isAlive) { _lastCc.Remove(playerId); continue; }

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

            // 3. V2.1: CC edge — нові прапори що тільки-но з'явились ((nowCc & ~lastCc) != 0).
            //    Гейтимо широким enemy-radius (2500) — щоб lasthit-dust на лайні без жодного
            //    видимого ворога не пінгав. Якщо хоч 1 ворог є в 2500 — фіримо.
            CcFlags lastCc = _lastCc.TryGetValue(playerId, out var lc) ? lc : CcFlags.None;
            CcFlags newCcBits = ally.Cc & ~lastCc;
            _lastCc[playerId] = ally.Cc;
            if (newCcBits != CcFlags.None && CcCooldownAllows(playerId, now))
            {
                int nearbyWide = CountEnemiesNear(enemies, wx, wy, 2500f);
                if (nearbyWide >= 1)
                {
                    ccTriggered.Add((playerId, wx, wy, newCcBits, nearbyWide));
                }
            }

            // 4. Δhp за вікнами 1с і 2с — старий-семпл-в-вікні мінус поточний.
            float dhp1 = ComputeDeltaHpOverWindow(buf, now, 1.0f);
            float dhp2 = ComputeDeltaHpOverWindow(buf, now, 2.0f);
            float dhp = MathF.Max(dhp1, dhp2);
            int nearby = CountEnemiesNear(enemies, wx, wy, EnemyProximityRadius);

            if (dhp >= SkirmishDeltaHp2s && nearby >= 1)
            {
                allyUnderAttack.Add((playerId, wx, wy, dhp2, dhp1, nearby));
            }
            else
            {
                // 5. V2.2: prefight-сигнали — тільки коли burst НЕ зафіксований цього тіку
                //    (інакше це вже фактично активний файт, prefight нерелевантний).
                if (PrefightCooldownAllows(playerId, now))
                {
                    // 5a. enemy cluster prefight: 3+ ворогів у 1500 без HP-drop'у.
                    if (nearby >= EnemyClusterMinCount)
                    {
                        prefightEvents.Add((playerId, wx, wy, "prefight_cluster"));
                    }
                    // 5b. isolated: ≥1 ворог поряд І найближчий ЖИВИЙ алі далі ніж 2000.
                    //     Гард: пропускаємо якщо в інпуті взагалі немає інших алі (тести
                    //     або edge-case коли GSI ще не наповнило team details).
                    else if (nearby >= IsolationMinEnemyCount && allies.Count >= 2)
                    {
                        float nearestOther = NearestOtherAllyDistance(allies, playerId, wx, wy);
                        if (nearestOther > IsolationMinAllyDistance)
                        {
                            prefightEvents.Add((playerId, wx, wy, "isolated"));
                        }
                    }
                }
            }
        }

        // 6. V2.1: CC-агрегація. ≥2 одночасно CCed алі → один Teamfight cluster_cc.
        //    Інакше — кожен окремо Skirmish cc_<flag>. cc_<hardCc> завжди ескалуємо до Teamfight,
        //    бо silence/disarm самі по собі рідко killable, але stun/hex = "виліт".
        if (ccTriggered.Count >= 2)
        {
            var ids = ccTriggered.Select(c => c.PlayerId).ToArray();
            // Епіцентр — середнє по позиціях зачеплених алі.
            float cx = ccTriggered.Average(c => c.Wx);
            float cy = ccTriggered.Average(c => c.Wy);
            events.Add(new FightEvent(now, cx, cy, FightSeverity.Teamfight, ids, "cluster_cc"));
            foreach (var c in ccTriggered) _ccCooldownUntil[c.PlayerId] = now.AddSeconds(CcCooldownSec);
        }
        else
        {
            foreach (var c in ccTriggered)
            {
                bool hard = (c.NewBits & CcFlags.HardDisable) != 0;
                var sev = hard ? FightSeverity.Teamfight : FightSeverity.Skirmish;
                string flag = (c.NewBits & CcFlags.Stunned) != 0 ? "stunned"
                             : (c.NewBits & CcFlags.Hexed) != 0 ? "hexed"
                             : (c.NewBits & CcFlags.Silenced) != 0 ? "silenced"
                             : "disarmed";
                events.Add(new FightEvent(now, c.Wx, c.Wy, sev, new[] { c.PlayerId }, "cc_" + flag));
                _ccCooldownUntil[c.PlayerId] = now.AddSeconds(CcCooldownSec);
            }
        }

        // 7. Класифікація severity з урахуванням кластеризації (existing).
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

        // 8. V2.2: випускаємо prefight-події (тільки ті алі що не в активному burst-cooldown'і).
        //    Дедуплікація: якщо для playerId уже emit'нули burst-подію в кроці 7 — prefight skip.
        var attackedIds = new HashSet<int>(allyUnderAttack.Select(a => a.PlayerId));
        foreach (var pf in prefightEvents)
        {
            if (attackedIds.Contains(pf.PlayerId)) continue;
            events.Add(new FightEvent(now, pf.Wx, pf.Wy,
                FightSeverity.Skirmish, new[] { pf.PlayerId }, pf.Reason));
            _prefightCooldownUntil[pf.PlayerId] = now.AddSeconds(PrefightCooldownSec);
        }

        return events;
    }

    /// <summary>Чи минув cooldown для цього алі (щоб не спамити пульсами на одну й ту ж бійку).</summary>
    private bool CooldownAllows(int playerId, DateTime now)
        => !_cooldownUntil.TryGetValue(playerId, out var until) || now >= until;

    private bool CcCooldownAllows(int playerId, DateTime now)
        => !_ccCooldownUntil.TryGetValue(playerId, out var until) || now >= until;

    private bool PrefightCooldownAllows(int playerId, DateTime now)
        => !_prefightCooldownUntil.TryGetValue(playerId, out var until) || now >= until;

    /// <summary>Дистанція до найближчого живого алі, окрім самого. <c>+∞</c> якщо немає.</summary>
    private static float NearestOtherAllyDistance(
        IReadOnlyList<AllyTickInput> allies, int selfPid, float wx, float wy)
    {
        float best2 = float.PositiveInfinity;
        foreach (var a in allies)
        {
            if (a.PlayerId == selfPid) continue;
            if (!a.IsAlive) continue;
            float dx = a.Wx - wx;
            float dy = a.Wy - wy;
            float d2 = dx * dx + dy * dy;
            if (d2 < best2) best2 = d2;
        }
        return float.IsPositiveInfinity(best2) ? float.PositiveInfinity : MathF.Sqrt(best2);
    }

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
/// <param name="Cc">V2.1: маска CC (Stunned/Hexed/Silenced/Disarmed) з <c>HeroDetails</c>.
/// Root/Slow GSI не віддає — тут їх нема.</param>
public readonly record struct AllyTickInput(
    int PlayerId,
    float HpPct,
    float MpPct,
    bool IsAlive,
    float Wx,
    float Wy,
    CcFlags Cc = CcFlags.None);
