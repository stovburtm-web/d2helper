namespace D2Helper.Core;

/// <summary>
/// Простий читач токенів з <c>secrets/tokens.env</c> (формат <c>KEY=value</c>, з підтримкою <c>#</c>-коментарів).
/// Fallback — environment variable з тим самим іменем.
/// Папка <c>secrets/</c> у gitignore — туди кладемо STRATZ_TOKEN, OPENDOTA_KEY, IMPRINT_KEY тощо.
/// </summary>
public static class TokenStore
{
    private static readonly object _lock = new();
    private static Dictionary<string, string>? _cache;

    public static string? Get(string key)
    {
        EnsureLoaded();
        if (_cache!.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        var env = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    private static void EnsureLoaded()
    {
        if (_cache is not null) return;
        lock (_lock)
        {
            if (_cache is not null) return;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = FindSecretsFile();
            if (path is not null && File.Exists(path))
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    dict[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }
            }
            _cache = dict;
        }
    }

    private static string? FindSecretsFile()
    {
        // Шукаємо secrets/tokens.env починаючи від AppContext.BaseDirectory вгору по дереву.
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "secrets", "tokens.env");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
