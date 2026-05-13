namespace D2Helper.Core;

/// <summary>Конвертації між форматами SteamID, що використовує Dota 2.</summary>
public static class SteamIdUtil
{
    private const long SteamId64Base = 76561197960265728L;

    /// <summary>Приймає або 64-бітний (76561...), або 32-бітний account id; повертає 64-бітний.</summary>
    public static bool TryParseSteamId64(string input, out long steamId64)
    {
        steamId64 = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.Trim();
        if (!long.TryParse(trimmed, out var value)) return false;
        if (value > SteamId64Base) { steamId64 = value; return true; }
        if (value > 0)             { steamId64 = SteamId64Base + value; return true; }
        return false;
    }

    public static long ToAccountId(long steamId64) => steamId64 - SteamId64Base;
}
