using D2Helper.Core.Quests;
using Xunit;

namespace D2Helper.Tests;

public class QuestHistoryTrackerTests
{
    private static QuestProgress Mk(string id, QuestStatus status, int current = 0, QuestGrade grade = QuestGrade.None)
        => new()
        {
            QuestId = id,
            Title = id,
            Type = QuestType.LastHits,
            Current = current,
            Target = 10,
            IsCompleted = status == QuestStatus.Completed,
            Status = status,
            Grade = grade,
        };

    [Fact]
    public void EmitsRecord_OnceWhenQuestCompletes()
    {
        var t = new QuestHistoryTracker("S1", "midlane.mvp");
        var snap = new GameStateSnapshot { ClockTime = 60 };

        var r1 = t.OnTick(snap, new[] { Mk("q1", QuestStatus.Active, 5) }, matchId: 999, heroName: "Lina");
        Assert.Empty(r1);

        var r2 = t.OnTick(snap, new[] { Mk("q1", QuestStatus.Completed, 10, QuestGrade.Good) }, 999, "Lina");
        Assert.Single(r2);
        Assert.Equal("q1", r2[0].QuestId);
        Assert.Equal(QuestStatus.Completed, r2[0].FinalStatus);
        Assert.Equal(QuestGrade.Good, r2[0].Grade);
        Assert.Equal(999, r2[0].MatchId);
        Assert.Equal("Lina", r2[0].HeroName);

        // ще один тік — не повинно бути дубля
        var r3 = t.OnTick(snap, new[] { Mk("q1", QuestStatus.Completed, 12, QuestGrade.Perfect) }, 999, "Lina");
        Assert.Empty(r3);
    }

    [Fact]
    public void EmitsRecord_WhenQuestExpires()
    {
        var t = new QuestHistoryTracker("S1", "midlane.mvp");
        var r = t.OnTick(new GameStateSnapshot { ClockTime = 200 },
            new[] { Mk("q1", QuestStatus.Expired, 2) }, null, null);
        Assert.Single(r);
        Assert.Equal(QuestStatus.Expired, r[0].FinalStatus);
    }

    [Fact]
    public void TracksMultipleQuestsIndependently()
    {
        var t = new QuestHistoryTracker("S1", "p");
        var snap = new GameStateSnapshot { ClockTime = 60 };
        var r = t.OnTick(snap, new[]
        {
            Mk("q1", QuestStatus.Completed, 10),
            Mk("q2", QuestStatus.Expired, 2),
            Mk("q3", QuestStatus.Active, 1),
        }, null, null);
        Assert.Equal(2, r.Count);
        Assert.Contains(r, x => x.QuestId == "q1");
        Assert.Contains(r, x => x.QuestId == "q2");
    }
}
