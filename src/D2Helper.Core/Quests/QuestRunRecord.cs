namespace D2Helper.Core.Quests;

/// <summary>
/// Запис в історію проходження квеста. Створюється коли квест переходить
/// у термінальний статус (Completed або Expired). Зберігається в SQLite.
/// </summary>
public sealed record QuestRunRecord
{
    public long Id { get; init; }
    public string SessionId { get; init; } = "";
    public string PlaybookId { get; init; } = "";
    public string QuestId { get; init; } = "";
    public string Title { get; init; } = "";
    public QuestType Type { get; init; }
    public long? MatchId { get; init; }
    public string? HeroName { get; init; }
    public int? FireAtClock { get; init; }
    public int? DueAtClock { get; init; }
    public int Target { get; init; }
    public int? TargetMin { get; init; }
    public int? TargetIdeal { get; init; }
    public int FinalCurrent { get; init; }
    public QuestGrade Grade { get; init; }
    public QuestStatus FinalStatus { get; init; }  // Completed або Expired
    public int? ClockStarted { get; init; }
    public int? ClockFinished { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime FinishedAtUtc { get; init; }
}
