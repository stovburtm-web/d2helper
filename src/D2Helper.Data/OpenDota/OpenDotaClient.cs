using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2Helper.Core;
using D2Helper.Core.Models;

namespace D2Helper.Data.OpenDota;

/// <summary>Клієнт до публічного OpenDota REST API.</summary>
public sealed class OpenDotaClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public OpenDotaClient(HttpClient http)
    {
        _http = http;
        if (_http.BaseAddress is null) _http.BaseAddress = new Uri("https://api.opendota.com/api/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("D2Helper/0.1 (+https://github.com/stovburtm-web/d2helper)");
    }

    /// <summary>
    /// GET з ретраєм на transient помилки (5xx, 429, network).
    /// OpenDota періодично віддає 500 — 3 спроби з backoff вирівнюють це.
    /// </summary>
    private async Task<HttpResponseMessage> GetWithRetryAsync(string path, CancellationToken ct)
    {
        const int maxAttempts = 3;
        var delays = new[] { 300, 800, 0 }; // ms; остання спроба без вайту
        HttpResponseMessage? resp = null;
        Exception? lastEx = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                resp?.Dispose();
                resp = await _http.GetAsync(path, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return resp;

                var status = (int)resp.StatusCode;
                var retriable = status >= 500 || status == 429;
                if (!retriable || attempt == maxAttempts) return resp;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                lastEx = ex;
            }
            catch (TaskCanceledException ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                lastEx = ex;
            }

            if (delays[attempt - 1] > 0)
                await Task.Delay(delays[attempt - 1], ct).ConfigureAwait(false);
        }

        if (resp is not null) return resp;
        throw lastEx ?? new HttpRequestException("OpenDota request failed");
    }

    public async Task<PlayerProfile?> GetPlayerAsync(long steamId64, CancellationToken ct = default)
    {
        var accountId = SteamIdUtil.ToAccountId(steamId64);
        using var resp = await GetWithRetryAsync($"players/{accountId}", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var profile = root.TryGetProperty("profile", out var p) ? p : default;
        var wl = await GetWinLossAsync(accountId, ct).ConfigureAwait(false);

        return new PlayerProfile
        {
            SteamId64 = steamId64,
            PersonaName = profile.ValueKind == JsonValueKind.Object && profile.TryGetProperty("personaname", out var pn) ? pn.GetString() ?? string.Empty : string.Empty,
            AvatarUrl = profile.ValueKind == JsonValueKind.Object && profile.TryGetProperty("avatarfull", out var av) ? av.GetString() ?? string.Empty : string.Empty,
            CountryCode = profile.ValueKind == JsonValueKind.Object && profile.TryGetProperty("loccountrycode", out var cc) ? cc.GetString() ?? string.Empty : string.Empty,
            RankTier = root.TryGetProperty("rank_tier", out var rt) && rt.ValueKind == JsonValueKind.Number ? rt.GetInt32() : null,
            Leaderboard = root.TryGetProperty("leaderboard_rank", out var lb) && lb.ValueKind == JsonValueKind.Number ? lb.GetInt32() : null,
            EstimatedMmr = root.TryGetProperty("mmr_estimate", out var me) && me.TryGetProperty("estimate", out var ee) && ee.ValueKind == JsonValueKind.Number ? ee.GetInt32() : null,
            Wins = wl.win,
            Losses = wl.loss,
            MatchesTotal = wl.win + wl.loss,
        };
    }

    private async Task<(int win, int loss)> GetWinLossAsync(long accountId, CancellationToken ct)
    {
        using var resp = await GetWithRetryAsync($"players/{accountId}/wl", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return (0, 0);
        var wl = await resp.Content.ReadFromJsonAsync<WinLossDto>(JsonOpts, ct).ConfigureAwait(false);
        return (wl?.Win ?? 0, wl?.Loss ?? 0);
    }

    public async Task<IReadOnlyList<RecentMatch>> GetRecentMatchesAsync(long steamId64, int limit = 20, CancellationToken ct = default)
    {
        var accountId = SteamIdUtil.ToAccountId(steamId64);
        using var resp = await GetWithRetryAsync($"players/{accountId}/recentMatches", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            // Кидаємо явну помилку зі статусом, щоб UI не маскував реальну причину
            // (rate-limit 429, 5xx, приватний профіль 200 з [] тощо).
            throw new HttpRequestException($"OpenDota /recentMatches HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} (after 3 retries)");
        }
        var raw = await resp.Content.ReadFromJsonAsync<List<RecentMatchDto>>(JsonOpts, ct).ConfigureAwait(false) ?? new();
        var heroes = await GetHeroNamesAsync(ct).ConfigureAwait(false);

        return raw.Take(limit).Select(m => new RecentMatch
        {
            MatchId = m.MatchId,
            HeroId = m.HeroId,
            HeroName = heroes.GetValueOrDefault(m.HeroId, $"Hero#{m.HeroId}"),
            // У Dota 2 player_slot < 128 → Radiant, >= 128 → Dire. Radiant_win → радіант виграв.
            Win = (m.PlayerSlot < 128) == m.RadiantWin,
            Kills = m.Kills,
            Deaths = m.Deaths,
            Assists = m.Assists,
            DurationSeconds = m.Duration,
            GoldPerMin = m.GoldPerMin,
            XpPerMin = m.XpPerMin,
            StartTime = DateTimeOffset.FromUnixTimeSeconds(m.StartTime),
            GameMode = GameModeName(m.GameMode),
            LobbyType = LobbyName(m.LobbyType),
        }).ToList();
    }

    private Dictionary<int, string>? _heroCache;
    private async Task<IReadOnlyDictionary<int, string>> GetHeroNamesAsync(CancellationToken ct)
    {
        if (_heroCache is not null) return _heroCache;
        using var resp = await GetWithRetryAsync("heroes", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return new Dictionary<int, string>();
        var heroes = await resp.Content.ReadFromJsonAsync<List<HeroDto>>(JsonOpts, ct).ConfigureAwait(false) ?? new();
        _heroCache = heroes.ToDictionary(h => h.Id, h => h.LocalizedName);
        return _heroCache;
    }

    private static string GameModeName(int id) => id switch
    {
        1 => "All Pick", 2 => "Captains Mode", 3 => "Random Draft",
        4 => "Single Draft", 5 => "All Random", 22 => "Ranked All Pick",
        23 => "Turbo", _ => $"Mode {id}",
    };

    private static string LobbyName(int id) => id switch
    {
        0 => "Public", 7 => "Ranked", _ => $"Lobby {id}",
    };

    private sealed class WinLossDto
    {
        [JsonPropertyName("win")] public int Win { get; set; }
        [JsonPropertyName("lose")] public int Loss { get; set; }
    }

    private sealed class HeroDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("localized_name")] public string LocalizedName { get; set; } = "";
    }

    private sealed class RecentMatchDto
    {
        [JsonPropertyName("match_id")] public long MatchId { get; set; }
        [JsonPropertyName("hero_id")] public int HeroId { get; set; }
        [JsonPropertyName("player_slot")] public int PlayerSlot { get; set; }
        [JsonPropertyName("radiant_win")] public bool RadiantWin { get; set; }
        [JsonPropertyName("kills")] public int Kills { get; set; }
        [JsonPropertyName("deaths")] public int Deaths { get; set; }
        [JsonPropertyName("assists")] public int Assists { get; set; }
        [JsonPropertyName("duration")] public int Duration { get; set; }
        [JsonPropertyName("gold_per_min")] public int GoldPerMin { get; set; }
        [JsonPropertyName("xp_per_min")] public int XpPerMin { get; set; }
        [JsonPropertyName("start_time")] public long StartTime { get; set; }
        [JsonPropertyName("game_mode")] public int GameMode { get; set; }
        [JsonPropertyName("lobby_type")] public int LobbyType { get; set; }
    }
}
