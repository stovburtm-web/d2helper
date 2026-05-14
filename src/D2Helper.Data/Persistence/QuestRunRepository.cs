using D2Helper.Core.Quests;
using Microsoft.Data.Sqlite;

namespace D2Helper.Data.Persistence;

public sealed class QuestRunRepository
{
    private readonly AppDatabase _db;

    public QuestRunRepository(AppDatabase db) { _db = db; }

    public void Add(QuestRunRecord r)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO quest_runs(
    session_id, playbook_id, quest_id, title, type,
    match_id, hero_name, fire_at_clock, due_at_clock,
    target, target_min, target_ideal, final_current,
    grade, final_status, clock_started, clock_finished,
    started_at_utc, finished_at_utc,
    score_awarded, streak_position
) VALUES (
    $session, $playbook, $quest, $title, $type,
    $match, $hero, $fire, $due,
    $target, $tmin, $tideal, $final,
    $grade, $status, $cstart, $cfin,
    $sat, $fat,
    $score, $streak
);";
        cmd.Parameters.AddWithValue("$session", r.SessionId);
        cmd.Parameters.AddWithValue("$playbook", r.PlaybookId);
        cmd.Parameters.AddWithValue("$quest", r.QuestId);
        cmd.Parameters.AddWithValue("$title", r.Title);
        cmd.Parameters.AddWithValue("$type", r.Type.ToString());
        cmd.Parameters.AddWithValue("$match", (object?)r.MatchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hero", (object?)r.HeroName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fire", (object?)r.FireAtClock ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$due", (object?)r.DueAtClock ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$target", r.Target);
        cmd.Parameters.AddWithValue("$tmin", (object?)r.TargetMin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tideal", (object?)r.TargetIdeal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$final", r.FinalCurrent);
        cmd.Parameters.AddWithValue("$grade", r.Grade.ToString());
        cmd.Parameters.AddWithValue("$status", r.FinalStatus.ToString());
        cmd.Parameters.AddWithValue("$cstart", (object?)r.ClockStarted ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cfin", (object?)r.ClockFinished ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sat", r.StartedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$fat", r.FinishedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$score", r.ScoreAwarded);
        cmd.Parameters.AddWithValue("$streak", (object?)r.StreakPosition ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<QuestRunRecord> GetRecent(int limit = 100)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, session_id, playbook_id, quest_id, title, type,
       match_id, hero_name, fire_at_clock, due_at_clock,
       target, target_min, target_ideal, final_current,
       grade, final_status, clock_started, clock_finished,
       started_at_utc, finished_at_utc,
       score_awarded, streak_position
FROM quest_runs
ORDER BY finished_at_utc DESC
LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<QuestRunRecord>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    /// <summary>
    /// Зведена статистика по всіх історичних квестах: для шапки UI та майбутніх рангів.
    /// Підраховує найдовший streak per-session (бо streak локальний до сесії).
    /// </summary>
    public ScoreAggregates GetAggregates()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    COALESCE(SUM(score_awarded), 0)                                   AS total_score,
    SUM(CASE WHEN grade='Perfect' AND final_status='Completed' THEN 1 ELSE 0 END) AS cnt_perfect,
    SUM(CASE WHEN grade='Good'    AND final_status='Completed' THEN 1 ELSE 0 END) AS cnt_good,
    SUM(CASE WHEN grade='Min'     AND final_status='Completed' THEN 1 ELSE 0 END) AS cnt_min,
    SUM(CASE WHEN final_status='Expired'                       THEN 1 ELSE 0 END) AS cnt_expired,
    COUNT(DISTINCT match_id)                                          AS matches_played,
    COALESCE(MAX(streak_position), 0)                                 AS longest_streak
FROM quest_runs;";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return ScoreAggregates.Empty;
        return new ScoreAggregates(
            TotalScore: r.GetInt64(0),
            Perfect: r.GetInt32(1),
            Good: r.GetInt32(2),
            Min: r.GetInt32(3),
            Expired: r.GetInt32(4),
            MatchesPlayed: r.IsDBNull(5) ? 0 : r.GetInt32(5),
            LongestStreak: r.GetInt32(6));
    }

    private static QuestRunRecord Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        SessionId = r.GetString(1),
        PlaybookId = r.GetString(2),
        QuestId = r.GetString(3),
        Title = r.GetString(4),
        Type = Enum.Parse<QuestType>(r.GetString(5)),
        MatchId = r.IsDBNull(6) ? null : r.GetInt64(6),
        HeroName = r.IsDBNull(7) ? null : r.GetString(7),
        FireAtClock = r.IsDBNull(8) ? null : r.GetInt32(8),
        DueAtClock = r.IsDBNull(9) ? null : r.GetInt32(9),
        Target = r.GetInt32(10),
        TargetMin = r.IsDBNull(11) ? null : r.GetInt32(11),
        TargetIdeal = r.IsDBNull(12) ? null : r.GetInt32(12),
        FinalCurrent = r.GetInt32(13),
        Grade = Enum.Parse<QuestGrade>(r.GetString(14)),
        FinalStatus = Enum.Parse<QuestStatus>(r.GetString(15)),
        ClockStarted = r.IsDBNull(16) ? null : r.GetInt32(16),
        ClockFinished = r.IsDBNull(17) ? null : r.GetInt32(17),
        StartedAtUtc = DateTime.Parse(r.GetString(18), null, System.Globalization.DateTimeStyles.RoundtripKind),
        FinishedAtUtc = DateTime.Parse(r.GetString(19), null, System.Globalization.DateTimeStyles.RoundtripKind),
        ScoreAwarded = r.IsDBNull(20) ? 0 : r.GetInt32(20),
        StreakPosition = r.IsDBNull(21) ? null : r.GetInt32(21),
    };
}

/// <summary>Зведений всечасовий рахунок гравця — основа майбутньої ранг-системи.</summary>
public sealed record ScoreAggregates(
    long TotalScore,
    int Perfect,
    int Good,
    int Min,
    int Expired,
    int MatchesPlayed,
    int LongestStreak)
{
    public static readonly ScoreAggregates Empty = new(0, 0, 0, 0, 0, 0, 0);
    public int TotalCompleted => Perfect + Good + Min;
    public int TotalQuests => TotalCompleted + Expired;
    /// <summary>% Completed від усіх емітнутих фінальних статусів. 0..100.</summary>
    public double CompletionRate => TotalQuests == 0 ? 0 : 100.0 * TotalCompleted / TotalQuests;
}
