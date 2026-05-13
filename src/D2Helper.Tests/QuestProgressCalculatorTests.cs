using D2Helper.Core.Quests;
using Xunit;

namespace D2Helper.Tests;

public class QuestProgressCalculatorTests
{
    private static readonly PlaybookDefinition Playbook = new()
    {
        Id = "test",
        Title = "test",
        Quests =
        [
            new QuestDefinition { Id = "g", Title = "Gold", Type = QuestType.GoldSpent, Target = 1500 },
            new QuestDefinition { Id = "d", Title = "Denies", Type = QuestType.Denies, Target = 8 },
            new QuestDefinition { Id = "w", Title = "Wards", Type = QuestType.WardsPlaced, Target = 4 },
            new QuestDefinition { Id = "z", Title = "Zone", Type = QuestType.PositionInZone, Target = 1, ZoneId = "zoneA" },
        ]
    };

    private static readonly ZoneCatalog Zones = new(new[]
    {
        new ZoneDefinition { Id = "zoneA", Name = "A", MinX = 0, MaxX = 100, MinY = 0, MaxY = 100 },
    });

    [Fact]
    public void CalculatesBasicCounters()
    {
        var snapshot = new GameStateSnapshot
        {
            GoldSpent = 900,
            Denies = 3,
            WardsPlaced = 2,
            PositionX = -50,
            PositionY = -50,
        };

        var result = QuestProgressCalculator.Calculate(Playbook, Zones, snapshot);

        Assert.Equal("900/1500", result[0].ProgressText);
        Assert.Equal("3/8", result[1].ProgressText);
        Assert.Equal("2/4", result[2].ProgressText);
        Assert.False(result[3].IsCompleted);
    }

    [Fact]
    public void CompletesPositionQuestWhenInsideZone()
    {
        var snapshot = new GameStateSnapshot
        {
            PositionX = 50,
            PositionY = 30,
        };

        var result = QuestProgressCalculator.Calculate(Playbook, Zones, snapshot);

        Assert.True(result[3].IsCompleted);
        Assert.Equal("1/1", result[3].ProgressText);
    }
}
