using D2Helper.Core.Quests;

namespace D2Helper.UI.ViewModels;

/// <summary>
/// Група квестів одного матчу для відображення в "Історія квестів".
/// Якщо MatchId невідомий — групуємо по SessionId.
/// </summary>
public sealed class QuestMatchGroup
{
    public string Header { get; init; } = "";
    public long? MatchId { get; init; }
    public string SessionId { get; init; } = "";
    public DateTime LatestFinishedAt { get; init; }
    public IReadOnlyList<QuestRunRecord> Quests { get; init; } = Array.Empty<QuestRunRecord>();

    public int CompletedCount => Quests.Count(q => q.FinalStatus == QuestStatus.Completed);
    public int ExpiredCount => Quests.Count(q => q.FinalStatus == QuestStatus.Expired);
    public string Summary => $"{CompletedCount} ✅ / {ExpiredCount} ⏰  ·  {Quests.Count} квестів";
}
