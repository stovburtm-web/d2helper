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
}

public sealed record PlaybookDefinition
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("quests")] public List<QuestDefinition> Quests { get; init; } = new();
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

