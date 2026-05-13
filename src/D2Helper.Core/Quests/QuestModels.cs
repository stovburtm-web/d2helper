using System.Text.Json.Serialization;

namespace D2Helper.Core.Quests;

public enum QuestType
{
    GoldSpent,
    Denies,
    WardsPlaced,
    PositionInZone,
    LastHits,
}

public sealed record QuestDefinition
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("type")] public QuestType Type { get; init; }
    [JsonPropertyName("target")] public int Target { get; init; }
    [JsonPropertyName("zoneId")] public string? ZoneId { get; init; }
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
    public string ProgressText => $"{Current}/{Target}";
};
