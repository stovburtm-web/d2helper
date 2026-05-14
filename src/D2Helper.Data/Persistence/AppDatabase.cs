using Microsoft.Data.Sqlite;

namespace D2Helper.Data.Persistence;

/// <summary>
/// Локальна SQLite БД в %LocalAppData%/D2Helper/d2helper.db.
/// Створює схему при першому запуску.
/// </summary>
public sealed class AppDatabase
{
    private readonly string _connectionString;

    public AppDatabase(string? overrideDbPath = null)
    {
        var path = overrideDbPath ?? DefaultDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = true,
        }.ToString();
    }

    public static string DefaultDbPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "D2Helper", "d2helper.db");
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    public void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS quest_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id      TEXT NOT NULL,
    playbook_id     TEXT NOT NULL,
    quest_id        TEXT NOT NULL,
    title           TEXT NOT NULL,
    type            TEXT NOT NULL,
    match_id        INTEGER NULL,
    hero_name       TEXT NULL,
    fire_at_clock   INTEGER NULL,
    due_at_clock    INTEGER NULL,
    target          INTEGER NOT NULL,
    target_min      INTEGER NULL,
    target_ideal    INTEGER NULL,
    final_current   INTEGER NOT NULL,
    grade           TEXT NOT NULL,
    final_status    TEXT NOT NULL,
    clock_started   INTEGER NULL,
    clock_finished  INTEGER NULL,
    started_at_utc  TEXT NOT NULL,
    finished_at_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_quest_runs_finished_at
    ON quest_runs(finished_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_quest_runs_session
    ON quest_runs(session_id);
";
        cmd.ExecuteNonQuery();
    }
}
