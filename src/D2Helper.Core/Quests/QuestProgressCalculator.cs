namespace D2Helper.Core.Quests;

public sealed record GameStateSnapshot
{
    public int ClockTime { get; init; }       // секунди від creep-spawn (може бути <0 в фазі підготовки)
    public int Gold { get; init; }            // поточне золото (reliable + unreliable)
    public int Level { get; init; }
    public int Xp { get; init; }              // не завжди дає GSI; може бути 0
    public int GoldSpent { get; init; }
    public int Denies { get; init; }
    public int WardsPlaced { get; init; }
    public int LastHits { get; init; }
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
    public int BottleCharges { get; init; }
    /// <summary>
    /// Кількість підібраних рун (Power/Water/Wisdom/Bounty — сума з GSI Player.RunesActivated).
    /// Точний тригер: інкрементиться рівно тоді, коли гравець реально активує руну
    /// (на відміну від gold-jump-евристики, яка може плутати з ласт-хітом великого крипа).
    /// </summary>
    public int RunesActivated { get; init; }
    /// <summary>
    /// Кількість застеканих лісових кемпів (Player.CampsStacked).
    /// Для майбутніх квестів P5 (саппорт стекає xx:53).
    /// </summary>
    public int CampsStacked { get; init; }
    public double? PositionX { get; init; }
    public double? PositionY { get; init; }
    public long? MatchId { get; init; }
    public string? HeroName { get; init; }
}

public static class QuestProgressCalculator
{
    // Простий безстатний калькулятор для лічильникових типів. Time-gated квести
    // потребують QuestScheduler який тримає baseline-state.
    public static IReadOnlyList<QuestProgress> Calculate(PlaybookDefinition playbook, ZoneCatalog zones, GameStateSnapshot snapshot)
    {
        var result = new List<QuestProgress>(playbook.Quests.Count);
        foreach (var q in playbook.Quests)
        {
            var target = Math.Max(1, q.Target);
            var current = q.Type switch
            {
                QuestType.GoldSpent => snapshot.GoldSpent,
                QuestType.Denies => snapshot.Denies,
                QuestType.WardsPlaced => snapshot.WardsPlaced,
                QuestType.LastHits => snapshot.LastHits,
                QuestType.LastHitsPlusDenies => snapshot.LastHits + snapshot.Denies,
                QuestType.LevelReach => snapshot.Level,
                QuestType.HasItem => snapshot.Items.Contains(q.ItemId ?? "") ? 1 : 0,
                QuestType.PositionInZone => InZone(q, zones, snapshot) ? 1 : 0,
                _ => 0,
            };
            var clamped = Math.Clamp(current, 0, target);
            var p01 = (double)clamped / target;
            result.Add(new QuestProgress
            {
                QuestId = q.Id,
                Title = q.Title,
                Type = q.Type,
                Current = current,
                Target = target,
                IsCompleted = current >= target,
                Progress01 = p01,
                Status = current >= target ? QuestStatus.Completed : QuestStatus.Active,
                FireAtClock = q.FireAtClock,
                DueAtClock = q.DueAtClock,
            });
        }
        return result;
    }

    private static bool InZone(QuestDefinition q, ZoneCatalog zones, GameStateSnapshot snapshot)
    {
        if (snapshot.PositionX is null || snapshot.PositionY is null) return false;
        return zones.IsInside(q.ZoneId, snapshot.PositionX.Value, snapshot.PositionY.Value);
    }
}

