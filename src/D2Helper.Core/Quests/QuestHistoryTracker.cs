namespace D2Helper.Core.Quests;

/// <summary>
/// Stateful трекер: дивиться послідовність (snapshot, progresses) і повертає
/// записи коли квести переходять у термінальний стан (Completed/Expired).
///
/// На один QuestId — максимум один запис за час життя трекера (тобто за сесію).
/// Якщо квест Completed — фіксуємо подію один раз, навіть якщо потім current
/// продовжить рости (для tiered ми "доростимо" final_current до моменту фіксації;
/// але якщо хочеться зафіксувати фінальний grade — викликай Finalize()).
/// </summary>
public sealed class QuestHistoryTracker
{
    private readonly string _sessionId;
    private readonly string _playbookId;
    private readonly Dictionary<string, QuestState> _state = new();

    // Поточний streak — кількість Completed квестів підряд (без Expired між ними).
    // Скидається на 0, коли будь-який квест завершується як Expired.
    // Спостерігається фінальними подіями у порядку їх емісії (тобто за часом fire/due).
    private int _currentStreak;
    private int _longestStreak;
    private int _sessionScore;

    public QuestHistoryTracker(string sessionId, string playbookId)
    {
        _sessionId = sessionId;
        _playbookId = playbookId;
    }

    /// <summary>Поточний streak (Completed-серія) в межах сесії.</summary>
    public int CurrentStreak => _currentStreak;
    /// <summary>Найдовший streak за сесію.</summary>
    public int LongestStreak => _longestStreak;
    /// <summary>Сума балів за квести цієї сесії (Score + StreakBonus).</summary>
    public int SessionScore => _sessionScore;

    private sealed class QuestState
    {
        public DateTime? StartedAtUtc;
        public int? ClockStarted;
        public bool FinalizedToDb;   // вже записаний у БД (Completed/Expired зафіксовано)
    }

    /// <summary>
    /// Прогнати tick: повертає список нових записів, які треба зберегти в БД.
    /// </summary>
    public IReadOnlyList<QuestRunRecord> OnTick(
        GameStateSnapshot snapshot,
        IReadOnlyList<QuestProgress> progresses,
        long? matchId,
        string? heroName)
    {
        List<QuestRunRecord>? finalized = null;
        var now = DateTime.UtcNow;

        foreach (var p in progresses)
        {
            if (!_state.TryGetValue(p.QuestId, out var st))
                st = _state[p.QuestId] = new QuestState();

            // Першого разу як побачили статус Active — фіксуємо старт.
            if (p.Status == QuestStatus.Active && st.StartedAtUtc is null)
            {
                st.StartedAtUtc = now;
                st.ClockStarted = snapshot.ClockTime;
            }

            // Термінальний стан — запис у БД (рівно один раз).
            if (!st.FinalizedToDb && (p.Status == QuestStatus.Completed || p.Status == QuestStatus.Expired))
            {
                st.FinalizedToDb = true;

                int? streakPos = null;
                int score = 0;
                if (p.Status == QuestStatus.Completed)
                {
                    _currentStreak++;
                    streakPos = _currentStreak;
                    if (_currentStreak > _longestStreak) _longestStreak = _currentStreak;
                    score = ScoreCalculator.Total(p.Status, p.Grade, _currentStreak);
                    _sessionScore += score;
                }
                else
                {
                    _currentStreak = 0;
                }

                finalized ??= new List<QuestRunRecord>();
                finalized.Add(new QuestRunRecord
                {
                    SessionId = _sessionId,
                    PlaybookId = _playbookId,
                    QuestId = p.QuestId,
                    Title = p.Title,
                    Type = p.Type,
                    MatchId = matchId,
                    HeroName = heroName,
                    FireAtClock = p.FireAtClock,
                    DueAtClock = p.DueAtClock,
                    Target = p.Target,
                    TargetMin = p.TargetMin,
                    TargetIdeal = p.TargetIdeal,
                    FinalCurrent = p.Current,
                    Grade = p.Grade,
                    FinalStatus = p.Status,
                    ClockStarted = st.ClockStarted,
                    ClockFinished = snapshot.ClockTime,
                    StartedAtUtc = st.StartedAtUtc ?? now,
                    FinishedAtUtc = now,
                    ScoreAwarded = score,
                    StreakPosition = streakPos,
                });
            }
        }

        return (IReadOnlyList<QuestRunRecord>?)finalized ?? Array.Empty<QuestRunRecord>();
    }
}
