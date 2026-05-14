using Dota2GSI;

namespace D2Helper.Core.Quests;

internal static class GameStateSnapshotExtractor
{
    public static GameStateSnapshot Extract(GameState gs)
    {
        // Прямий доступ до полів Dota2GSI: усе нижче приходить і для свого
        // гравця в ranked-матчі (на відміну від HeroDamage / WardsPlaced /
        // RunesActivated — вони spectator-only).
        var player = gs.Player?.LocalPlayer;
        var hero = gs.Hero?.LocalPlayer;
        var items = gs.Items?.LocalPlayer;

        // Збираємо назви предметів з інвентарю + стешу + нейтралки + телепорта.
        // "empty" слоти пропускаємо, бо інакше HasItem-перевірка завжди true.
        var names = new List<string>(12);
        int bottleCharges = 0;
        if (items != null)
        {
            foreach (var i in items.Inventory)
            {
                if (string.IsNullOrEmpty(i.Name) || i.Name == "empty") continue;
                names.Add(i.Name);
                // Bottle charges (включно з "item_bottle_filled_*" для павер-рун).
                if (i.Name.StartsWith("item_bottle")) bottleCharges = Math.Max(bottleCharges, i.Charges);
            }
            foreach (var i in items.Stash) if (!string.IsNullOrEmpty(i.Name) && i.Name != "empty") names.Add(i.Name);
            if (!string.IsNullOrEmpty(items.Neutral?.Name) && items.Neutral.Name != "empty") names.Add(items.Neutral.Name);
            if (!string.IsNullOrEmpty(items.Teleport?.Name) && items.Teleport.Name != "empty") names.Add(items.Teleport.Name);
        }

        return new GameStateSnapshot
        {
            ClockTime = gs.Map?.ClockTime ?? 0,
            Gold = player?.Gold ?? 0,
            Level = hero?.Level ?? 0,
            Xp = hero?.Experience ?? 0,
            GoldSpent = player?.ItemGoldSpent ?? 0,
            Denies = player?.Denies ?? 0,
            WardsPlaced = player?.WardsPlaced ?? 0,
            LastHits = player?.LastHits ?? 0,
            Items = names,
            BottleCharges = bottleCharges,
            // GSI v2 поля (точніші, ніж евристика по золоту).
            // Player.RunesActivated рахує всі підняті руни — bounty/power/water/wisdom.
            RunesActivated = player?.RunesActivated ?? 0,
            CampsStacked = player?.CampsStacked ?? 0,
            PositionX = hero?.Location.X,
            PositionY = hero?.Location.Y,
            MatchId = gs.Map?.MatchID is long mid && mid > 0 ? mid : null,
            HeroName = string.IsNullOrEmpty(hero?.Name) ? null : hero!.Name,
        };
    }
}

