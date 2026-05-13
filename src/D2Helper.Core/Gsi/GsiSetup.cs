using Microsoft.Win32;

namespace D2Helper.Core.Gsi;

/// <summary>
/// Допоміжні утиліти роботи з GSI-конфігом Dota 2 — пошук Steam-папки через registry,
/// перелік усіх Steam library folders, безпечне видалення нашого cfg.
/// </summary>
public static class GsiSetup
{
    /// <summary>
    /// Повертає список усіх ймовірних шляхів до <c>gamestate_integration_{name}.cfg</c>,
    /// у яких міг опинитися наш файл (через всі Steam library folders).
    /// </summary>
    public static IReadOnlyList<string> FindLikelyCfgPaths(string name)
    {
        var result = new List<string>();
        foreach (var dotaRoot in EnumerateDotaInstalls())
        {
            var cfgDir = Path.Combine(dotaRoot, "game", "dota", "cfg", "gamestate_integration");
            var file = Path.Combine(cfgDir, $"gamestate_integration_{name}.cfg");
            result.Add(file);
        }
        return result;
    }

    public static IEnumerable<string> EnumerateDotaInstalls()
    {
        foreach (var steamRoot in EnumerateSteamRoots())
        {
            foreach (var library in EnumerateLibraries(steamRoot))
            {
                var dota = Path.Combine(library, "steamapps", "common", "dota 2 beta");
                if (Directory.Exists(dota)) yield return dota;
            }
        }
    }

    private static IEnumerable<string> EnumerateSteamRoots()
    {
        if (!OperatingSystem.IsWindows()) yield break;

        string? path = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            path = key?.GetValue("SteamPath") as string;
        }
        catch { /* ignore */ }
        if (!string.IsNullOrEmpty(path)) yield return path.Replace('/', '\\');

        foreach (var fallback in new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            @"D:\Steam",
        })
        {
            if (Directory.Exists(fallback)) yield return fallback;
        }
    }

    private static IEnumerable<string> EnumerateLibraries(string steamRoot)
    {
        // Спочатку — сам steamRoot як library.
        yield return steamRoot;

        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        // Дуже простий парсер VDF: шукаємо рядки `"path"\s+"value"`.
        foreach (var raw in File.ReadAllLines(vdf))
        {
            var line = raw.Trim();
            if (!line.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
            var first = line.IndexOf('"', 6);
            if (first < 0) continue;
            var second = line.IndexOf('"', first + 1);
            if (second < 0) continue;
            var value = line.Substring(first + 1, second - first - 1).Replace(@"\\", @"\");
            if (Directory.Exists(value)) yield return value;
        }
    }
}
