using D2Helper.Core.Quests;
using Xunit;

namespace D2Helper.Tests;

public class ScoreCalculatorTests
{
    [Theory]
    [InlineData(QuestStatus.Completed, QuestGrade.Perfect, 3)]
    [InlineData(QuestStatus.Completed, QuestGrade.Good, 2)]
    [InlineData(QuestStatus.Completed, QuestGrade.Min, 1)]
    [InlineData(QuestStatus.Completed, QuestGrade.None, 0)]
    [InlineData(QuestStatus.Expired, QuestGrade.Perfect, 0)] // expired => 0, навіть якщо grade успадкувався
    [InlineData(QuestStatus.Active, QuestGrade.Good, 0)]     // нефінальний статус => 0
    public void Score_AwardsByGrade_OnlyForCompleted(QuestStatus status, QuestGrade grade, int expected)
    {
        Assert.Equal(expected, ScoreCalculator.Score(status, grade));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(7, 2)]
    [InlineData(8, 3)]
    [InlineData(99, 3)]
    public void StreakBonus_Tiers(int position, int expected)
    {
        Assert.Equal(expected, ScoreCalculator.StreakBonus(position));
    }

    [Fact]
    public void Total_CombinesScoreAndStreakBonus_ForCompleted()
    {
        // Perfect (3) + streak position 5 bonus (2) = 5
        Assert.Equal(5, ScoreCalculator.Total(QuestStatus.Completed, QuestGrade.Perfect, 5));
    }

    [Fact]
    public void Total_IsZero_ForExpiredRegardlessOfStreak()
    {
        Assert.Equal(0, ScoreCalculator.Total(QuestStatus.Expired, QuestGrade.Perfect, 99));
    }
}

public class QuestHistoryTrackerScoreTests
{
    private static QuestProgress Mk(string id, QuestStatus status, QuestGrade grade = QuestGrade.None)
        => new()
        {
            QuestId = id,
            Title = id,
            Type = QuestType.LastHits,
            Current = 10,
            Target = 10,
            IsCompleted = status == QuestStatus.Completed,
            Status = status,
            Grade = grade,
        };

    [Fact]
    public void Streak_IncrementsOnCompleted_ResetsOnExpired()
    {
        var t = new QuestHistoryTracker("S1", "p");
        var snap = new GameStateSnapshot { ClockTime = 60 };

        // 3 completed підряд → streak 1,2,3 (бонус починається з 3)
        var r1 = t.OnTick(snap, new[] { Mk("q1", QuestStatus.Completed, QuestGrade.Good) }, null, null);
        Assert.Equal(1, r1[0].StreakPosition);
        Assert.Equal(2, r1[0].ScoreAwarded); // Good=2, bonus(1)=0

        var r2 = t.OnTick(snap, new[] { Mk("q2", QuestStatus.Completed, QuestGrade.Perfect) }, null, null);
        Assert.Equal(2, r2[0].StreakPosition);
        Assert.Equal(3, r2[0].ScoreAwarded); // Perfect=3, bonus(2)=0

        var r3 = t.OnTick(snap, new[] { Mk("q3", QuestStatus.Completed, QuestGrade.Min) }, null, null);
        Assert.Equal(3, r3[0].StreakPosition);
        Assert.Equal(2, r3[0].ScoreAwarded); // Min=1, bonus(3)=1

        Assert.Equal(3, t.CurrentStreak);
        Assert.Equal(3, t.LongestStreak);
        Assert.Equal(7, t.SessionScore);

        // Expired — обнуляє поточний streak, але не longest.
        var r4 = t.OnTick(snap, new[] { Mk("q4", QuestStatus.Expired) }, null, null);
        Assert.Null(r4[0].StreakPosition);
        Assert.Equal(0, r4[0].ScoreAwarded);
        Assert.Equal(0, t.CurrentStreak);
        Assert.Equal(3, t.LongestStreak);

        // Наступний Completed знов починає з 1.
        var r5 = t.OnTick(snap, new[] { Mk("q5", QuestStatus.Completed, QuestGrade.Perfect) }, null, null);
        Assert.Equal(1, r5[0].StreakPosition);
        Assert.Equal(3, r5[0].ScoreAwarded);
    }
}
