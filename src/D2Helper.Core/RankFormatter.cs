namespace D2Helper.Core;

/// <summary>
/// Декодує Dota 2 <c>rank_tier</c> (число типу <c>71</c>) у людську назву <c>"Divine 1"</c>.
/// Формат: десятки = медаль (1-8), одиниці = кількість зірок (1-5). 80 і вище = Immortal.
/// </summary>
public static class RankFormatter
{
    public static string Render(int? rankTier, int? leaderboardRank = null)
    {
        if (rankTier is null or 0) return "Uncalibrated";
        var t = rankTier.Value;
        if (t >= 80)
            return leaderboardRank is > 0 ? $"Immortal #{leaderboardRank}" : "Immortal";
        var medal = t / 10;
        var star = t % 10;
        var name = medal switch
        {
            1 => "Herald",
            2 => "Guardian",
            3 => "Crusader",
            4 => "Archon",
            5 => "Legend",
            6 => "Ancient",
            7 => "Divine",
            _ => "Unknown",
        };
        return star is >= 1 and <= 5 ? $"{name} {star}" : name;
    }
}
