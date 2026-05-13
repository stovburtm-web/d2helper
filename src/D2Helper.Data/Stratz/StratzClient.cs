using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2Helper.Core;

namespace D2Helper.Data.Stratz;

/// <summary>Мінімальний клієнт до Stratz GraphQL. API-ключ опційний (інакше — анонімні ліміти).</summary>
public sealed class StratzClient
{
    private const string Endpoint = "https://api.stratz.com/graphql";
    private readonly HttpClient _http;

    public StratzClient(HttpClient http, string? apiToken = null)
    {
        _http = http;
        if (!string.IsNullOrWhiteSpace(apiToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        // STRATZ API вимагає цей точний User-Agent інакше віддає 403.
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("STRATZ_API");
    }

    public string? LastError { get; private set; }

    public async Task<StratzPlayerSummary?> GetPlayerSummaryAsync(long steamId64, CancellationToken ct = default)
    {
        LastError = null;
        var accountId = SteamIdUtil.ToAccountId(steamId64);
        var query = $$"""
        { player(steamAccountId: {{accountId}}) {
            steamAccount { name avatar isAnonymous seasonRank seasonLeaderboardRank }
            ranks { rank asOfDateTime }
            matchCount winCount
            behaviorScore
            firstMatchDate
            lastMatchDate
            imp
        } }
        """;
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { query }),
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            LastError = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {Truncate(json, 1000)}";
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errors", out var errs))
        {
            LastError = "GraphQL errors: " + Truncate(errs.GetRawText(), 1000);
            return null;
        }
        if (!doc.RootElement.TryGetProperty("data", out var data)) { LastError = "no data field"; return null; }
        if (!data.TryGetProperty("player", out var p) || p.ValueKind != JsonValueKind.Object) { LastError = "player is null"; return null; }

        var account = p.TryGetProperty("steamAccount", out var sa) ? sa : default;

        // ranks[] — історія рангів; беремо найсвіжіший за asOfDateTime.
        int? latestRank = null;
        if (p.TryGetProperty("ranks", out var ranksArr) && ranksArr.ValueKind == JsonValueKind.Array && ranksArr.GetArrayLength() > 0)
        {
            long bestTs = long.MinValue;
            foreach (var r in ranksArr.EnumerateArray())
            {
                var ts = TryLong(r, "asOfDateTime") ?? 0;
                var rk = TryInt(r, "rank");
                if (rk is null) continue;
                if (ts >= bestTs) { bestTs = ts; latestRank = rk; }
            }
        }

        return new StratzPlayerSummary
        {
            Name = TryStr(account, "name"),
            Avatar = TryStr(account, "avatar"),
            SeasonRank = latestRank ?? TryInt(account, "seasonRank"),
            LeaderboardRank = TryInt(account, "seasonLeaderboardRank"),
            MatchCount = TryInt(p, "matchCount") ?? 0,
            WinCount = TryInt(p, "winCount") ?? 0,
            BehaviorScore = TryInt(p, "behaviorScore"),
            Imp = TryInt(p, "imp"),
            LastMatchUnix = TryLong(p, "lastMatchDate"),
        };
    }

    private static long? TryLong(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt64() : null;

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    private static string TryStr(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static int? TryInt(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : null;
}

public sealed record StratzPlayerSummary
{
    public string Name { get; init; } = "";
    public string Avatar { get; init; } = "";
    public int? SeasonRank { get; init; }
    public int? LeaderboardRank { get; init; }
    public int MatchCount { get; init; }
    public int WinCount { get; init; }
    public int? BehaviorScore { get; init; }
    public int? Imp { get; init; }
    public long? LastMatchUnix { get; init; }
    public double WinRate => MatchCount == 0 ? 0 : (double)WinCount / MatchCount;
    public DateTimeOffset? LastMatch => LastMatchUnix is > 0 ? DateTimeOffset.FromUnixTimeSeconds(LastMatchUnix.Value) : null;
}
