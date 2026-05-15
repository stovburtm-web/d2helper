using System.Net.Http;
using System.Text.Json;
using D2Helper.Data.OpenDota;
using D2Helper.Knowledge.Cli;

// Простий CLI що:
//   1) Через OpenDota Data Explorer (/api/explorer?sql=...) знаходить parsed-матчі
//      з MMR 3000..6000 на EU кластерах за останні 30 днів.
//   2) Для кожного матчу витягає /api/matches/{id}, парсить teamfights[].players[].deaths_pos.
//   3) Агрегує в DeathHeatmapAggregator (128×128 grid, 5 time-bins, 2 sides).
//   4) Зберігає в data/death-heatmap-{patch}-eu-{mmrMin}to{mmrMax}.bin.
//
// Free-tier OpenDota: 60 req/min → 1000 матчів ≈ 17 хв чистого fetch'у.
// Мінімальні аргументи: --limit, --mmr-min, --mmr-max, --patch, --out, --no-region-filter.

int limit = 1000;
int mmrMin = 3000;
int mmrMax = 6000;
string patch = "7.41c";
bool noRegionFilter = false;
string? outOverride = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--limit": limit = int.Parse(args[++i]); break;
        case "--mmr-min": mmrMin = int.Parse(args[++i]); break;
        case "--mmr-max": mmrMax = int.Parse(args[++i]); break;
        case "--patch": patch = args[++i]; break;
        case "--out": outOverride = args[++i]; break;
        case "--no-region-filter": noRegionFilter = true; break;
        case "--help":
        case "-h":
            Console.WriteLine("Usage: D2Helper.Knowledge.Cli [--limit N] [--mmr-min N] [--mmr-max N] [--patch v] [--out path] [--no-region-filter]");
            return 0;
    }
}

var outPath = outOverride ?? Path.Combine("data", $"death-heatmap-{patch}-eu-{mmrMin/1000}to{mmrMax/1000}k.bin");
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

// OpenDota більше не зберігає avg_mmr у public_matches — тільки avg_rank_tier (10..85).
// Приблизне мапування MMR → rank_tier:
//   Herald(1): 0-700, Guardian(2): 700-1400, Crusader(3): 1400-2100, Archon(4): 2100-2800,
//   Legend(5): 2800-3700, Ancient(6): 3700-4600, Divine(7): 4600-5500, Immortal(8): 5500+.
// Кожен медал має 5 зірок → крок ≈ 140 MMR на зірку.
static int MmrToTier(int mmr)
{
    int league = mmr switch
    {
        < 700 => 1, < 1400 => 2, < 2100 => 3, < 2800 => 4,
        < 3700 => 5, < 4600 => 6, < 5500 => 7, _ => 8,
    };
    int leagueLow = league switch { 1 => 0, 2 => 700, 3 => 1400, 4 => 2100, 5 => 2800, 6 => 3700, 7 => 4600, _ => 5500 };
    int leagueRange = league < 8 ? 700 : 2500;
    int stars = Math.Clamp((mmr - leagueLow) * 5 / leagueRange, 0, 5);
    return league * 10 + stars;
}
int tierMin = MmrToTier(mmrMin);
int tierMax = MmrToTier(mmrMax);

Console.WriteLine($"[K] target: limit={limit} mmr={mmrMin}..{mmrMax} (rank_tier {tierMin}..{tierMax}) patch={patch} out={outPath} region={(noRegionFilter ? "any" : "EU")}");

using var http = new HttpClient();
var od = new OpenDotaClient(http);

// EU clusters (OpenDota constants): EU west/east. Підбір не точний — гра в основному в цих cluster'ах.
//   181..188 = EU West (Stockholm, Frankfurt, Vienna, Madrid, Helsinki, Warsaw...),
//   200 = EU East (RUS-Moscow), 204 = SA. 211/215/225/227 — supplementary EU/RU/ME.
// Беремо широкий набір; якщо потрібен лише EUWest — звузити пізніше.
var euClusters = new[] { 181, 184, 185, 186, 187, 188, 191, 192, 200, 211, 215, 224, 225, 227 };

// SQL: parsed-матчі (matches.version IS NOT NULL → є teamfights), avg_mmr у діапазоні, недавні.
// SQL працює коли ORDER BY match_id (primary key index), а не start_time.
// matches.cluster і pm.avg_rank_tier дають EU + MMR filter одночасно.
// matches.version IS NOT NULL гарантує parsed (є teamfights).
var sql =
    "SELECT m.match_id " +
    "FROM matches m " +
    "JOIN public_matches pm USING(match_id) " +
    "WHERE m.version IS NOT NULL " +
    $"  AND pm.avg_rank_tier BETWEEN {tierMin} AND {tierMax}" +
    "  AND m.duration BETWEEN 900 AND 5400 " +
    (noRegionFilter ? "" : $"  AND m.cluster IN ({string.Join(",", euClusters)}) ") +
    "ORDER BY m.match_id DESC " +
    $"LIMIT {limit}";

Console.WriteLine($"[K] SQL: {sql}");

using var idsDoc = await od.ExplorerSqlAsync(sql);
var rows = idsDoc.RootElement.TryGetProperty("rows", out var r) ? r : default;
if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
{
    Console.Error.WriteLine($"[K] Explorer returned no rows. Розглянь --no-region-filter або ширший MMR.");
    if (idsDoc.RootElement.TryGetProperty("err", out var err))
        Console.Error.WriteLine($"[K] err: {err}");
    return 1;
}

var matchIds = new List<long>();
foreach (var row in rows.EnumerateArray())
{
    if (row.TryGetProperty("match_id", out var mid) && mid.TryGetInt64(out var v))
        matchIds.Add(v);
}
Console.WriteLine($"[K] got {matchIds.Count} match IDs");

var agg = new DeathHeatmapAggregator();
int okCount = 0, failCount = 0, noTfCount = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();

// Free tier: 60/min → щонайменше 1.05 sec між запитами. Закладаю 1100мс для запасу.
const int rateLimitMs = 1100;

for (int i = 0; i < matchIds.Count; i++)
{
    var id = matchIds[i];
    JsonDocument? matchDoc = null;
    try { matchDoc = await od.GetMatchAsync(id); }
    catch (Exception ex)
    {
        Console.WriteLine($"[K] {i+1}/{matchIds.Count} match {id}: ERR {ex.Message}");
        failCount++;
        await Task.Delay(rateLimitMs);
        continue;
    }
    if (matchDoc is null) { failCount++; await Task.Delay(rateLimitMs); continue; }

    using (matchDoc)
    {
        var root = matchDoc.RootElement;
        if (!root.TryGetProperty("teamfights", out var tfs) || tfs.ValueKind != JsonValueKind.Array || tfs.GetArrayLength() == 0)
        {
            noTfCount++;
        }
        else
        {
            int matchDeaths = 0;
            foreach (var tf in tfs.EnumerateArray())
            {
                int tfStart = tf.TryGetProperty("start", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : 0;
                if (!tf.TryGetProperty("players", out var pls) || pls.ValueKind != JsonValueKind.Array) continue;

                int slotIdx = -1;
                foreach (var pl in pls.EnumerateArray())
                {
                    slotIdx++;
                    bool isRadiant = slotIdx < 5;
                    if (!pl.TryGetProperty("deaths_pos", out var dp) || dp.ValueKind != JsonValueKind.Object) continue;
                    foreach (var col in dp.EnumerateObject())
                    {
                        if (!int.TryParse(col.Name, out var cy)) continue;
                        foreach (var cell in col.Value.EnumerateObject())
                        {
                            if (!int.TryParse(cell.Name, out var cx)) continue;
                            var count = cell.Value.ValueKind == JsonValueKind.Number ? cell.Value.GetInt32() : 0;
                            for (int k = 0; k < count; k++) agg.Add(cx, cy, tfStart, isRadiant);
                            matchDeaths += count;
                        }
                    }
                }
            }
            okCount++;
            if ((i+1) % 25 == 0 || i == 0)
                Console.WriteLine($"[K] {i+1}/{matchIds.Count} ok={okCount} fail={failCount} noTf={noTfCount} totalDeaths={agg.TotalDeaths} elapsed={sw.Elapsed:mm\\:ss}");
        }
    }

    // Rate limit
    await Task.Delay(rateLimitMs);
}

sw.Stop();
Console.WriteLine($"[K] DONE in {sw.Elapsed:mm\\:ss}: ok={okCount} fail={failCount} noTf={noTfCount}");
Console.WriteLine(agg.SummarizeForLogging());

var bytes = agg.Serialize();
await File.WriteAllBytesAsync(outPath, bytes);
Console.WriteLine($"[K] saved {bytes.Length} bytes → {outPath}");
return 0;
