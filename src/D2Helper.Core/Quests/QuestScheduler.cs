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
        public int BottleChargesAtFire;
        public bool Completed;
        public int? CompletedAtClock;
    }

    /// <summary>
    /// Скільки секунд після виконання квест буде підсвічений та закріплений у верху overlay
    /// (вікно “вкусняшки” — гравець має встигнути побачити фідбек).
    /// </summary>
    public const int CelebrationWindowSec = 7;

    /// <summary>
    /// За скільки секунд до due_at квест починає пульсувати ("ГАЙ!")
    /// </summary>
    public const int DeadlineWarningSec = 30;

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

            var goal = Math.Max(1, q.Target);
            var min = Math.Max(1, q.TargetMin ?? goal);
            var ideal = Math.Max(goal, q.TargetIdeal ?? goal);

            // Pending: ще не настав fire_at (і ще не виконано).
            if (q.FireAtClock is int fire && clock < fire && !st.Completed)
            {
                result.Add(BuildProgress(q, current: 0, goal, min, ideal,
                    status: QuestStatus.Pending, done: false));
                continue;
            }

            // Перший раз потрапили у вікно — фіксуємо baseline.
            if (!st.BaselineCaptured)
            {
                st.BaselineCaptured = true;
                st.GoldAtFire = snapshot.Gold;
                st.XpAtFire = snapshot.Xp;
                st.LevelAtFire = snapshot.Level;
                st.BottleChargesAtFire = snapshot.BottleCharges;
            }

            // Рахуємо поточний прогрес (продовжуємо рости навіть після min — для tiered).
            var current = ComputeCurrent(q, snapshot, st);
            var clamped = Math.Clamp(current, 0, ideal);

            // ✅ "Виконано" фіксується по min-порогу і вже не знімається.
            if (current >= min)
            {
                if (!st.Completed) st.CompletedAtClock = clock;
                st.Completed = true;
            }

            // Expired: вікно зачинилось, min не досягнутий.
            var expired = q.DueAtClock is int due && clock > due && !st.Completed;
            var status = st.Completed ? QuestStatus.Completed
                       : expired ? QuestStatus.Expired
                       : QuestStatus.Active;

            var celebrating = st.Completed
                && st.CompletedAtClock is int cac
                && (clock - cac) <= CelebrationWindowSec;

            // Дедлайн-warning: тільки для Active, коли до due_at лишилося ≤ N секунд.
            var deadlineSoon = status == QuestStatus.Active
                && q.DueAtClock is int dueClk
                && (dueClk - clock) <= DeadlineWarningSec
                && (dueClk - clock) > 0;

            result.Add(BuildProgress(q, clamped, goal, min, ideal, status, st.Completed,
                completedAtClock: st.CompletedAtClock, isCelebrating: celebrating,
                isDeadlineSoon: deadlineSoon));
        }

        return result;
    }

    private static QuestProgress BuildProgress(
        QuestDefinition q, int current, int goal, int min, int ideal,
        QuestStatus status, bool done,
        int? completedAtClock = null, bool isCelebrating = false,
        bool isDeadlineSoon = false)
    {
        var grade = current >= ideal ? QuestGrade.Perfect
                  : current >= goal  ? QuestGrade.Good
                  : current >= min   ? QuestGrade.Min
                  : QuestGrade.None;

        return new QuestProgress
        {
            QuestId = q.Id,
            Title = q.Title,
            Type = q.Type,
            Current = current,
            Target = ideal,           // прогрес-бар йде до ідеалу
            IsCompleted = done,
            Progress01 = (double)current / Math.Max(1, ideal),
            Status = status,
            FireAtClock = q.FireAtClock,
            DueAtClock = q.DueAtClock,
            TargetMin = q.TargetMin,
            TargetIdeal = q.TargetIdeal,
            Grade = grade,
            CompletedAtClock = completedAtClock,
            IsCelebrating = isCelebrating,
            IsDeadlineSoon = isDeadlineSoon,
        };
    }

    /// <summary>
    /// Бере з повного списку до 3-х квестів для показу в overlay.
    /// Пріоритет:
    ///   1) Celebrating (тільки-но виконані — вкуснячка, пінимо вверху на 7с);
    ///   2) усі Active (не виконані у вікні),
    ///   3) рівно 1 найсвіжіше "старе" Completed (✅ sticky, щоб гравець бачив прогрес),
    ///   4) заповнюємо залишок Pending у порядку наближення fire_at.
    /// Expired не показуємо (програні квести не варто "мозолити").
    /// </summary>
    public static IReadOnlyList<QuestProgress> SelectVisible(IReadOnlyList<QuestProgress> all)
    {
        var celebrating = all.Where(q => q.IsCelebrating).ToList();
        var active = all.Where(q => q.Status == QuestStatus.Active).ToList();
        var stickyCompleted = all
            .Where(q => q.Status == QuestStatus.Completed && !q.IsCelebrating)
            .TakeLast(1).ToList();
        var pending = all.Where(q => q.Status == QuestStatus.Pending)
                         .OrderBy(q => q.FireAtClock ?? int.MaxValue)
                         .ToList();

        var visible = new List<QuestProgress>(VisibleSlotCount);
        visible.AddRange(celebrating.Take(VisibleSlotCount));
        if (visible.Count < VisibleSlotCount) visible.AddRange(active.Take(VisibleSlotCount - visible.Count));
        if (visible.Count < VisibleSlotCount) visible.AddRange(stickyCompleted.Take(VisibleSlotCount - visible.Count));
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
            QuestType.PickRune => (
                // 1) bottle charges стрибнули вгору (water/power picked up in bottle)
                (s.BottleCharges > st.BottleChargesAtFire) ||
                // 2) gold-jump >= threshold (bounty rune, або у когось без bottle)
                (s.Gold - st.GoldAtFire) >= (q.GoldJumpThreshold ?? 40)
            ) ? 1 : 0,
            QuestType.WisdomRune => (s.Level > st.LevelAtFire) || (s.Xp - st.XpAtFire) >= (q.XpJumpThreshold ?? 100) ? 1 : 0,
            _ => 0,
        };
    }
}
