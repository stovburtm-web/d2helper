using D2Helper.Core.Quests;
using Xunit;

namespace D2Helper.Tests;

public class QuestSchedulerTests
{
    private static QuestDefinition CounterQuest(string id, QuestType type, int target, int fire, int due)
        => new() { Id = id, Title = id, Type = type, Target = target, FireAtClock = fire, DueAtClock = due };

    private static PlaybookDefinition One(QuestDefinition q) => new() { Id = "t", Title = "t", Quests = [q] };

    [Fact]
    public void Pending_BeforeFireAt()
    {
        var pb = One(CounterQuest("lh", QuestType.LastHits, 20, fire: 180, due: 300));
        var s = new QuestScheduler();
        var r = s.Tick(pb, new GameStateSnapshot { ClockTime = 60, LastHits = 5 });
        Assert.Equal(QuestStatus.Pending, r[0].Status);
        Assert.Equal(0, r[0].Current); // прогрес ховаємо до fire_at
    }

    [Fact]
    public void Active_InsideWindow()
    {
        var pb = One(CounterQuest("lh", QuestType.LastHits, 20, fire: 180, due: 300));
        var s = new QuestScheduler();
        var r = s.Tick(pb, new GameStateSnapshot { ClockTime = 200, LastHits = 12 });
        Assert.Equal(QuestStatus.Active, r[0].Status);
        Assert.Equal(12, r[0].Current);
    }

    [Fact]
    public void Completed_WhenTargetReached()
    {
        var pb = One(CounterQuest("lh", QuestType.LastHits, 20, fire: 180, due: 300));
        var s = new QuestScheduler();
        var r = s.Tick(pb, new GameStateSnapshot { ClockTime = 250, LastHits = 22 });
        Assert.Equal(QuestStatus.Completed, r[0].Status);
        Assert.True(r[0].IsCompleted);
    }

    [Fact]
    public void Completed_StaysCompleted_AfterDue()
    {
        var pb = One(CounterQuest("lh", QuestType.LastHits, 20, fire: 180, due: 300));
        var s = new QuestScheduler();
        s.Tick(pb, new GameStateSnapshot { ClockTime = 250, LastHits = 22 });
        var r = s.Tick(pb, new GameStateSnapshot { ClockTime = 700, LastHits = 22 });
        Assert.Equal(QuestStatus.Completed, r[0].Status); // лишається висіти ✅
    }

    [Fact]
    public void Expired_WhenWindowPasses()
    {
        var pb = One(CounterQuest("lh", QuestType.LastHits, 20, fire: 180, due: 300));
        var s = new QuestScheduler();
        // у вікні набрали лише 5 ластів
        s.Tick(pb, new GameStateSnapshot { ClockTime = 200, LastHits = 5 });
        var r = s.Tick(pb, new GameStateSnapshot { ClockTime = 310, LastHits = 5 });
        Assert.Equal(QuestStatus.Expired, r[0].Status);
    }

    [Fact]
    public void PickRune_DetectsGoldJump()
    {
        var q = new QuestDefinition { Id = "r", Title = "rune", Type = QuestType.PickRune, Target = 1, FireAtClock = 110, DueAtClock = 135, GoldJumpThreshold = 40 };
        var s = new QuestScheduler();
        // На fire_at: baseline gold = 500
        s.Tick(One(q), new GameStateSnapshot { ClockTime = 110, Gold = 500 });
        // Gold не виріс → ще Active
        var mid = s.Tick(One(q), new GameStateSnapshot { ClockTime = 120, Gold = 510 });
        Assert.Equal(QuestStatus.Active, mid[0].Status);
        // +50 → Completed
        var done = s.Tick(One(q), new GameStateSnapshot { ClockTime = 130, Gold = 555 });
        Assert.Equal(QuestStatus.Completed, done[0].Status);
    }

    [Fact]
    public void HasItem_FindsItemInInventory()
    {
        var q = new QuestDefinition { Id = "b", Title = "bottle", Type = QuestType.HasItem, Target = 1, ItemId = "item_bottle", FireAtClock = 30, DueAtClock = 60 };
        var s = new QuestScheduler();
        var without = s.Tick(One(q), new GameStateSnapshot { ClockTime = 45, Items = new[] { "item_tango" } });
        Assert.Equal(QuestStatus.Active, without[0].Status);
        var with = s.Tick(One(q), new GameStateSnapshot { ClockTime = 50, Items = new[] { "item_tango", "item_bottle" } });
        Assert.Equal(QuestStatus.Completed, with[0].Status);
    }

    [Fact]
    public void LevelReach_Completed()
    {
        var q = new QuestDefinition { Id = "l", Title = "lvl6", Type = QuestType.LevelReach, Target = 6, FireAtClock = 330, DueAtClock = 360 };
        var s = new QuestScheduler();
        var r = s.Tick(One(q), new GameStateSnapshot { ClockTime = 340, Level = 6 });
        Assert.Equal(QuestStatus.Completed, r[0].Status);
    }

    [Fact]
    public void WisdomRune_DetectsLevelUp()
    {
        var q = new QuestDefinition { Id = "w", Title = "wisdom", Type = QuestType.WisdomRune, Target = 1, FireAtClock = 390, DueAtClock = 435 };
        var s = new QuestScheduler();
        s.Tick(One(q), new GameStateSnapshot { ClockTime = 390, Level = 5, Xp = 1000 });
        // ловимо level-up
        var r = s.Tick(One(q), new GameStateSnapshot { ClockTime = 420, Level = 6, Xp = 1100 });
        Assert.Equal(QuestStatus.Completed, r[0].Status);
    }

    [Fact]
    public void SelectVisible_PrefersActiveOverCompletedOverPending()
    {
        var all = new List<QuestProgress>
        {
            new() { QuestId = "p1", Status = QuestStatus.Pending, FireAtClock = 600 },
            new() { QuestId = "a1", Status = QuestStatus.Active },
            new() { QuestId = "c1", Status = QuestStatus.Completed },
            new() { QuestId = "p2", Status = QuestStatus.Pending, FireAtClock = 400 },
            new() { QuestId = "a2", Status = QuestStatus.Active },
            new() { QuestId = "e1", Status = QuestStatus.Expired },
        };
        var visible = QuestScheduler.SelectVisible(all);
        Assert.Equal(3, visible.Count);
        Assert.Contains(visible, q => q.QuestId == "a1");
        Assert.Contains(visible, q => q.QuestId == "a2");
        Assert.Contains(visible, q => q.QuestId == "c1");
        Assert.DoesNotContain(visible, q => q.QuestId == "e1");
    }

    [Fact]
    public void SelectVisible_CompletedCappedAtOne_LeavesRoomForPending()
    {
        // Кейс з реальної гри: гравець виконав 3 квести підряд,
        // но наступний Pending має все одно бути видимим як прев'ю.
        var all = new List<QuestProgress>
        {
            new() { QuestId = "c1", Status = QuestStatus.Completed },
            new() { QuestId = "c2", Status = QuestStatus.Completed },
            new() { QuestId = "c3", Status = QuestStatus.Completed },
            new() { QuestId = "p1", Status = QuestStatus.Pending, FireAtClock = 300 },
            new() { QuestId = "p2", Status = QuestStatus.Pending, FireAtClock = 500 },
        };
        var visible = QuestScheduler.SelectVisible(all);
        Assert.Equal(3, visible.Count);
        Assert.Equal("c3", visible[0].QuestId); // останнє Completed
        Assert.Equal("p1", visible[1].QuestId);
        Assert.Equal("p2", visible[2].QuestId);
    }

    [Fact]
    public void SelectVisible_FillsWithPendingByEarliestFire()
    {
        var all = new List<QuestProgress>
        {
            new() { QuestId = "p1", Status = QuestStatus.Pending, FireAtClock = 600 },
            new() { QuestId = "p2", Status = QuestStatus.Pending, FireAtClock = 200 },
            new() { QuestId = "p3", Status = QuestStatus.Pending, FireAtClock = 400 },
        };
        var visible = QuestScheduler.SelectVisible(all);
        Assert.Equal(3, visible.Count);
        Assert.Equal("p2", visible[0].QuestId); // найближчий по часу
        Assert.Equal("p3", visible[1].QuestId);
        Assert.Equal("p1", visible[2].QuestId);
    }

    [Fact]
    public void QuestWithoutFireAt_IsAlwaysActive()
    {
        var q = new QuestDefinition { Id = "lh", Title = "lh", Type = QuestType.LastHits, Target = 100 };
        var s = new QuestScheduler();
        var r = s.Tick(One(q), new GameStateSnapshot { ClockTime = 0, LastHits = 5 });
        Assert.Equal(QuestStatus.Active, r[0].Status);
    }
}
