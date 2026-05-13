namespace D2Helper.Core.Models;

/// <summary>Профіль гравця, агрегований із кількох джерел.</summary>
public sealed record PlayerProfile
{
    public long SteamId64 { get; init; }
    public long AccountId => SteamId64 - 76561197960265728L;
    public string PersonaName { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public int? RankTier { get; init; }          // 0..85 з OpenDota
    public int? Leaderboard { get; init; }       // абсолютна позиція якщо Immortal
    public int? EstimatedMmr { get; init; }      // зі Stratz, якщо є
    public int MatchesTotal { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public double WinRate => MatchesTotal == 0 ? 0 : (double)Wins / MatchesTotal;
    public DateTimeOffset? LastMatchTime { get; init; }

    public string RankName => RankTier switch
    {
        null or 0 => "Uncalibrated",
        >= 80 => $"Immortal{(Leaderboard.HasValue ? $" #{Leaderboard}" : string.Empty)}",
        _ => RenderRank(RankTier.Value),
    };

    private static string RenderRank(int tier)
    {
        var medal = tier / 10;
        var star = tier % 10;
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
        return $"{name} {star}";
    }
}
