namespace D2Helper.Core.Models;

public sealed record RecentMatch
{
    public long MatchId { get; init; }
    public int HeroId { get; init; }
    public string HeroName { get; init; } = string.Empty;
    public bool Win { get; init; }
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public int DurationSeconds { get; init; }
    public int GoldPerMin { get; init; }
    public int XpPerMin { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public string LobbyType { get; init; } = string.Empty;
    public string GameMode { get; init; } = string.Empty;

    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
    public string KdaString => $"{Kills}/{Deaths}/{Assists}";
    public string Result => Win ? "WIN" : "LOSS";
}
