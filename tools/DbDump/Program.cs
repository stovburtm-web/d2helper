using Microsoft.Data.Sqlite;

var db = args.Length > 0 ? args[0]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Helper", "d2helper.db");
Console.WriteLine($"DB: {db}");

using var c = new SqliteConnection($"Data Source={db}");
c.Open();

using (var cmd = c.CreateCommand())
{
    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
    using var r = cmd.ExecuteReader();
    Console.Write("Tables:");
    while (r.Read()) Console.Write(" " + r.GetString(0));
    Console.WriteLine();
}

using (var cmd = c.CreateCommand())
{
    cmd.CommandText = "PRAGMA table_info(quest_runs)";
    using var r = cmd.ExecuteReader();
    Console.Write("Columns:");
    while (r.Read()) Console.Write(" " + r["name"]);
    Console.WriteLine();
}

using (var cmd = c.CreateCommand())
{
    cmd.CommandText = "SELECT * FROM quest_runs ORDER BY id DESC LIMIT 100";
    using var r = cmd.ExecuteReader();
    var cols = new string[r.FieldCount];
    for (int i = 0; i < r.FieldCount; i++) cols[i] = r.GetName(i);
    Console.WriteLine(string.Join(" | ", cols));
    while (r.Read())
    {
        var row = new string[r.FieldCount];
        for (int i = 0; i < r.FieldCount; i++) row[i] = (r.IsDBNull(i) ? "" : r.GetValue(i)?.ToString()) ?? "";
        Console.WriteLine(string.Join(" | ", row));
    }
}
