namespace D2Helper.Core.Quests;

/// <summary>
/// Stateful планувальник квестів. Тримає baseline (gold/xp/level на момент
/// fire_at) для кожного квеста і обчислює його статус на основі clock + snapshot.
///
/// Контракт:
///  - Pending  поки clock &lt; fire_at;
///  - Active   у вікні [fire_at, due_at], якщо умова ще не виконана;
///  - Completed коли умова виконана (фіксуємо стан, далі не змінюється);
///  - Expired  коли clock &gt; due_at і не Completed.
///
/// Quests без fire_at вважаються "always-active" (поводяться як старий калькулятор).
/// </summary>
public sealed class QuestScheduler
{
    private readonly Dictionary<string, QuestState> _state = new();

    private sealed class QuestState
    {
        public bool BaselineCaptured;
        public int GoldAtFire;
        public int XpAtFire;
        public int LevelAtFire;
        public bool Completed;
        public int CompletedCurrent;
        public int CompletedTarget;
    }

    /// <summary>
    /// Кількість активних/виконаних квестів, які треба показати в overlay.
    /// </summary>
    public const int VisibleSlotCount = 3;

    public IReadOnlyList<QuestProgress> Tick(PlaybookDefinition playbook, GameStateSnapshot snapshot)
    {
        var clock = snapshot.ClockTime;
        var result = new List<QuestProgress>(playbook.Quests.Count);

        foreach (var q in playbook.Quests)
        {
            var st = _state.TryGetValue(q.Id, out var existing) ? existing : (_state[q.Id] = new QuestState());

            // Якщо вже відмічений Completed — лишаємо його як є.
            if (st.Completed)
            {
                result.Add(new QuestProgress
                {
                    QuestId = q.Id,
                    Title = q.Title,
                    Type = q.Type,
                    Current = st.CompletedCurrent,
                    Target = st.CompletedTarget,
                    IsCompleted = true,
                    Progress01 = 1.0,
                    Status = QuestStatus.Completed,
                    FireAtClock = q.FireAtClock,
                    DueAtClock = q.DueAtClock,
                });
                continue;
            }

            // Pending: ще не настав fire_at.
            if (q.FireAtClock is int fire && clock < fire)
            {
                result.Add(new QuestProgress
                {
                    QuestId = q.Id,
                    Title = q.Title,
                    Type = q.Type,
                    Current = 0,
                    Target = Math.Max(1, q.Target),
                    IsCompleted = false,
                    Progress01 = 0,
                    Status = QuestStatus.Pending,
                    FireAtClock = q.FireAtClock,
                    DueAtClock = q.DueAtClock,
                });
                continue;
            }

            // Перший раз потрапили у вікно — фіксуємо baseline.
            if (!st.BaselineCaptured)
            {
                st.BaselineCaptured = true;
                st.GoldAtFire = snapshot.Gold;
                st.XpAtFire = snapshot.Xp;
                st.LevelAtFire = snapshot.Level;
            }

            // Active: рахуємо прогрес.
            var target = Math.Max(1, q.Target);
            var current = ComputeCurrent(q, snapshot, st);
            var clamped = Math.Clamp(current, 0, target);
            var done = current >= target;

            // Expired: вікно минуло, не виконано.
            var expired = q.DueAtClock is int due && clock > due && !done;

            var status = done ? QuestStatus.Completed : expired ? QuestStatus.Expired : QuestStatus.Active;

            if (done)
            {
                st.Completed = true;
                st.CompletedCurrent = clamped;
                st.CompletedTarget = target;
            }

            result.Add(new QuestProgress
            {
                QuestId = q.Id,
                Title = q.Title,
                Type = q.Type,
                Current = clamped,
                Target = target,
                IsCompleted = done,
                Progress01 = (double)clamped / target,
                Status = status,
                FireAtClock = q.FireAtClock,
                DueAtClock = q.DueAtClock,
            });
        }

        return result;
    }

    /// <summary>
    /// Бере з повного списку до 3-х квестів для показу в overlay.
    /// Пріоритет:
    ///   1) усі Active (не виконані у вікні),
    ///   2) рівно 1 найсвіжіше Completed (✅ sticky, щоб гравець бачив фідбек),
    ///   3) заповнюємо залишок Pending у порядку наближення fire_at.
    /// Expired не показуємо (програні квести не варто "мозолити").
    /// </summary>
    public static IReadOnlyList<QuestProgress> SelectVisible(IReadOnlyList<QuestProgress> all)
    {
        var active = all.Where(q => q.Status == QuestStatus.Active).ToList();
        var lastCompleted = all.Where(q => q.Status == QuestStatus.Completed).TakeLast(1).ToList();
        var pending = all.Where(q => q.Status == QuestStatus.Pending)
                         .OrderBy(q => q.FireAtClock ?? int.MaxValue)
                         .ToList();

        var visible = new List<QuestProgress>(VisibleSlotCount);
        visible.AddRange(active.Take(VisibleSlotCount));
        if (visible.Count < VisibleSlotCount) visible.AddRange(lastCompleted.Take(VisibleSlotCount - visible.Count));
        if (visible.Count < VisibleSlotCount) visible.AddRange(pending.Take(VisibleSlotCount - visible.Count));
        return visible.Take(VisibleSlotCount).ToList();
    }

    private static int ComputeCurrent(QuestDefinition q, GameStateSnapshot s, QuestState st)
    {
        return q.Type switch
        {
            QuestType.GoldSpent => s.GoldSpent,
            QuestType.Denies => s.Denies,
            QuestType.WardsPlaced => s.WardsPlaced,
            QuestType.LastHits => s.LastHits,
            QuestType.LastHitsPlusDenies => s.LastHits + s.Denies,
            QuestType.LevelReach => s.Level,
            QuestType.HasItem => s.Items.Contains(q.ItemId ?? "") ? 1 : 0,
            QuestType.PickRune => (s.Gold - st.GoldAtFire) >= (q.GoldJumpThreshold ?? 40) ? 1 : 0,
            QuestType.WisdomRune => (s.Level > st.LevelAtFire) || (s.Xp - st.XpAtFire) >= (q.XpJumpThreshold ?? 100) ? 1 : 0,
            _ => 0,
        };
    }
}
