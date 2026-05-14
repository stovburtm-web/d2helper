using System.Text.Json.Serialization;

namespace D2Helper.Core.Quests;

public enum QuestType
{
    // legacy / прості лічильники
    GoldSpent,
    Denies,
    WardsPlaced,
    PositionInZone,
    LastHits,
    // v2: time-gated
    LastHitsPlusDenies,
    LevelReach,
    HasItem,
    PickRune,           // Gold-jump у вікні fire→due (бавнті/павер)
    WisdomRune,         // XP-spike або level-up у вікні fire→due
}

public enum QuestStatus
{
    Pending,    // ще не настав fire_at
    Active,     // у вікні fire_at..due_at, ще не виконано
    Completed,  // умова виконана
    Expired,    // вікно зачинилось, умова не виконана
}

public sealed record QuestDefinition
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("type")] public QuestType Type { get; init; }
    [JsonPropertyName("target")] public int Target { get; init; }
    [JsonPropertyName("zoneId")] public string? ZoneId { get; init; }

    // v2 поля — таймлайн і додаткові аргументи
    [JsonPropertyName("fireAtClock")] public int? FireAtClock { get; init; }
    [JsonPropertyName("dueAtClock")] public int? DueAtClock { get; init; }
    [JsonPropertyName("itemId")] public string? ItemId { get; init; }
    [JsonPropertyName("goldJumpThreshold")] public int? GoldJumpThreshold { get; init; }
    [JsonPropertyName("xpJumpThreshold")] public int? XpJumpThreshold { get; init; }

    // Трирівнева градація виконання (опціональна).
    // Якщо вказана тільки Target — єдиний рівень "виконано/ні".
    // Якщо вказані TargetMin/TargetIdeal — quest вважається виконаним при current ≥ TargetMin,
    // але прогрес-бар продовжує рости до TargetIdeal — для візуальної градації (мін/норм/ідеал).
    [JsonPropertyName("targetMin")] public int? TargetMin { get; init; }
    [JsonPropertyName("targetIdeal")] public int? TargetIdeal { get; init; }
}

public sealed record PlaybookDefinition
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("quests")] public List<QuestDefinition> Quests { get; init; } = new();
}

public enum QuestGrade
{
    None,      // current < min
    Min,       // current ≥ min, < norm
    Good,      // current ≥ norm, < ideal
    Perfect,   // current ≥ ideal
}

public sealed record QuestProgress
{
    public string QuestId { get; init; } = "";
    public string Title { get; init; } = "";
    public QuestType Type { get; init; }
    public int Current { get; init; }
    public int Target { get; init; }
    public bool IsCompleted { get; init; }
    public double Progress01 { get; init; }
    public QuestStatus Status { get; init; }
    public int? FireAtClock { get; init; }
    public int? DueAtClock { get; init; }
    public int? TargetMin { get; init; }
    public int? TargetIdeal { get; init; }
    public QuestGrade Grade { get; init; } = QuestGrade.None;
    /// <summary>
    /// Clock-time коли квест перейшов у Completed (фіксується один раз).
    /// </summary>
    public int? CompletedAtClock { get; init; }
    /// <summary>
    /// Квест виконано недавно — треба програти анімацію та принижати в overlay кілька секунд.
    /// </summary>
    public bool IsCelebrating { get; init; }
    /// <summary>
    /// Active квест, до дедлайну лишилося ≤ N секунд — потрібна пульсуюча анімація щоб вибити з фокусу гри.
    /// </summary>
    public bool IsDeadlineSoon { get; init; }
    public string ProgressText => $"{Current}/{Target}";
    public string TimeWindow => (FireAtClock, DueAtClock) switch
    {
        (int f, int d) => $"{FormatClock(f)}→{FormatClock(d)}",
        (int f, null) => $"з {FormatClock(f)}",
        _ => "",
    };
    private static string FormatClock(int seconds)
    {
        var sign = seconds < 0 ? "-" : "";
        var abs = Math.Abs(seconds);
        return $"{sign}{abs / 60:D2}:{abs % 60:D2}";
    }
}

