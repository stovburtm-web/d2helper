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
    started_at_utc, finished_at_utc
) VALUES (
    $session, $playbook, $quest, $title, $type,
    $match, $hero, $fire, $due,
    $target, $tmin, $tideal, $final,
    $grade, $status, $cstart, $cfin,
    $sat, $fat
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
       started_at_utc, finished_at_utc
FROM quest_runs
ORDER BY finished_at_utc DESC
LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<QuestRunRecord>();
        while (r.Read()) list.Add(Map(r));
        return list;
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
    };
}
