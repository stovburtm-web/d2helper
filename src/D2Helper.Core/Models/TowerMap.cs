namespace D2Helper.Core.Models;

/// <summary>V1.7: ідентифікатор конкретної вежі.</summary>
public enum TowerKey
{
    T1Top, T1Mid, T1Bot,
    T2Top, T2Mid, T2Bot,
    T3Top, T3Mid, T3Bot,
    T4Left, T4Right,
}

/// <summary>Команда вежі.</summary>
public enum TowerTeam { Radiant, Dire }

/// <summary>
/// Хардкод world-координат і tier-ваг для всіх 22 веж Dota 2 (11 на сторону).
///
/// Координати взяті з community-данних (Dotabuff/Reddit) і округлені на 50 unit'ів —
/// точність ±200 unit нас влаштовує, бо радіус aura ~1100 unit. Тестами не покриваємо
/// абсолютні значення, лише відносні (ця вежа лівіше тієї і т.д.).
/// </summary>
public static class TowerMap
{
    /// <summary>Tier-вага вежі: пізні вежі дають більше danger/safety на own/enemy зоні.</summary>
    public static float TierWeight(TowerKey key) => key switch
    {
        TowerKey.T1Top or TowerKey.T1Mid or TowerKey.T1Bot => 0.8f,
        TowerKey.T2Top or TowerKey.T2Mid or TowerKey.T2Bot => 1.0f,
        TowerKey.T3Top or TowerKey.T3Mid or TowerKey.T3Bot => 1.2f,
        TowerKey.T4Left or TowerKey.T4Right => 1.5f,
        _ => 1.0f,
    };

    /// <summary>Радіус aura в world-units (σ гауса). На r=σ внесок ~37% від центрального.
    /// V1.7.1: збільшено з 1100 до 2000 — щоб сусідні вежі на лайні (~3500 unit apart) формували
    /// суцільне поле, а не точкові «острівці». Інакше absence-crush крушив зони між вежами в зелене.</summary>
    public const float AuraSigma = 2000f;

    /// <summary>Координати веж Radiant (нижній-лівий кут мапи).</summary>
    public static readonly IReadOnlyDictionary<TowerKey, (float X, float Y)> Radiant =
        new Dictionary<TowerKey, (float, float)>
        {
            { TowerKey.T1Top,  (-6100f,  1850f) },
            { TowerKey.T1Mid,  (-1600f,  -400f) },
            { TowerKey.T1Bot,  ( 4950f, -6150f) },
            { TowerKey.T2Top,  (-6250f,  -700f) },
            { TowerKey.T2Mid,  (-3550f, -2600f) },
            { TowerKey.T2Bot,  (    0f, -6350f) },
            { TowerKey.T3Top,  (-6650f, -3250f) },
            { TowerKey.T3Mid,  (-4750f, -4300f) },
            { TowerKey.T3Bot,  (-3600f, -6050f) },
            { TowerKey.T4Left, (-5750f, -4850f) },
            { TowerKey.T4Right,(-4900f, -5750f) },
        };

    /// <summary>Координати веж Dire (верхній-правий кут мапи).</summary>
    public static readonly IReadOnlyDictionary<TowerKey, (float X, float Y)> Dire =
        new Dictionary<TowerKey, (float, float)>
        {
            { TowerKey.T1Top,  (-4750f,  6150f) },
            { TowerKey.T1Mid,  ( 1850f,   300f) },
            { TowerKey.T1Bot,  ( 6250f, -1500f) },
            { TowerKey.T2Top,  (-1500f,  6350f) },
            { TowerKey.T2Mid,  ( 3550f,  2650f) },
            { TowerKey.T2Bot,  ( 6350f,   700f) },
            { TowerKey.T3Top,  ( 3600f,  6050f) },
            { TowerKey.T3Mid,  ( 4850f,  4300f) },
            { TowerKey.T3Bot,  ( 6550f,  3250f) },
            { TowerKey.T4Left, ( 4900f,  5700f) },
            { TowerKey.T4Right,( 5750f,  4800f) },
        };

    /// <summary>Повертає координати вежі для конкретної команди.</summary>
    public static (float X, float Y) GetPosition(TowerTeam team, TowerKey key)
        => team == TowerTeam.Radiant ? Radiant[key] : Dire[key];

    /// <summary>Усі (team, key) пари — для ітерації при семплюванні.</summary>
    public static IEnumerable<(TowerTeam Team, TowerKey Key)> All()
    {
        foreach (var k in Radiant.Keys) yield return (TowerTeam.Radiant, k);
        foreach (var k in Dire.Keys)    yield return (TowerTeam.Dire, k);
    }
}
