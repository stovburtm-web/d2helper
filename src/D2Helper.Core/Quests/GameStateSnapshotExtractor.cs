using System.Globalization;
using Dota2GSI;

namespace D2Helper.Core.Quests;

internal static class GameStateSnapshotExtractor
{
    public static GameStateSnapshot Extract(GameState gs)
    {
        // Прямий доступ до полів Dota2GSI: LastHits/Denies приходять і в демці.
        // WardsPlaced / ItemGoldSpent — spectator-only, тож у звичайній грі = 0.
        var player = gs.Player?.LocalPlayer;
        return new GameStateSnapshot
        {
            GoldSpent = player?.ItemGoldSpent ?? 0,
            Denies = player?.Denies ?? 0,
            WardsPlaced = player?.WardsPlaced ?? 0,
            LastHits = player?.LastHits ?? 0,
            PositionX = null,
            PositionY = null,
        };
    }
}
