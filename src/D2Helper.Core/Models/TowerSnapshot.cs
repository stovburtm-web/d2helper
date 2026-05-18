namespace D2Helper.Core.Models;

/// <summary>
/// V1.7: знімок стану всіх веж у конкретний момент. Тримає alive-флаг для кожної з 22 веж
/// (11 Radiant + 11 Dire) і вміє повертати signed aura у будь-якій world-точці:
/// <list type="bullet">
/// <item>живі ворожі вежі → <b>+danger</b> (за гаусом, з вагою tier'а);</item>
/// <item>живі союзні вежі → <b>−danger</b> (safety pull);</item>
/// <item>мертві вежі — внеску немає.</item>
/// </list>
/// Цей сигнал перебиває absence-crush у зонах де реально стоїть ворожа вежа: ми не можемо
/// сказати «у ворожому Т1-боті безпечно» просто тому що 5 крапок зараз згрупувалися деінде.
/// </summary>
public sealed class TowerSnapshot
{
    private readonly Dictionary<(TowerTeam, TowerKey), bool> _alive;

    /// <summary>Створює знімок з конкретними станами. Відсутній ключ = жива вежа (defensive).</summary>
    public TowerSnapshot(Dictionary<(TowerTeam, TowerKey), bool> alive)
    {
        _alive = alive;
    }

    /// <summary>Дефолт: усі 22 вежі живі (стан на 0:00). Використовується для тестів і fallback'а.</summary>
    public static TowerSnapshot AllAlive()
    {
        var d = new Dictionary<(TowerTeam, TowerKey), bool>();
        foreach (var (team, key) in TowerMap.All())
            d[(team, key)] = true;
        return new TowerSnapshot(d);
    }

    /// <summary>Перевіряє чи вежа жива (відсутній ключ → true, defensive).</summary>
    public bool IsAlive(TowerTeam team, TowerKey key)
        => !_alive.TryGetValue((team, key), out var v) || v;

    /// <summary>Створює нову копію з оновленим станом однієї вежі (для застосування TowerDestroyed).</summary>
    public TowerSnapshot WithDestroyed(TowerTeam team, TowerKey key)
    {
        var d = new Dictionary<(TowerTeam, TowerKey), bool>(_alive)
        {
            [(team, key)] = false,
        };
        return new TowerSnapshot(d);
    }

    /// <summary>
    /// Семпл signed aura в точці (wx, wy) для гравця за вказану сторону.
    /// Повертає <c>+enemyAura − friendlySafety</c>, де кожна вежа додає
    /// <c>tierWeight × exp(−r²/σ²)</c>. Soft-clamped у [−1.5..+1.5].
    /// </summary>
    /// <param name="playerIsRadiant"><c>true</c> якщо гравець за Radiant — тоді Dire вежі ворожі.</param>
    public float SampleAura(float wx, float wy, bool playerIsRadiant)
    {
        float sigma2 = TowerMap.AuraSigma * TowerMap.AuraSigma;
        float enemy = 0f, friendly = 0f;
        var enemyTeam = playerIsRadiant ? TowerTeam.Dire : TowerTeam.Radiant;

        foreach (var (team, key) in TowerMap.All())
        {
            if (!IsAlive(team, key)) continue;
            var (tx, ty) = TowerMap.GetPosition(team, key);
            float dx = wx - tx;
            float dy = wy - ty;
            float r2 = dx * dx + dy * dy;
            float kernel = (float)Math.Exp(-r2 / sigma2);
            float contrib = TowerMap.TierWeight(key) * kernel;
            if (team == enemyTeam) enemy += contrib;
            else friendly += contrib;
        }

        float signed = enemy - friendly;
        if (signed >  1.5f) signed =  1.5f;
        if (signed < -1.5f) signed = -1.5f;
        return signed;
    }

    /// <summary>
    /// V1.8.2: structural «tower coverage» — наскільки геометричний baseDanger підкріплений
    /// реально живими ворожими будівлями поряд. Якщо їх нема — модельний червоний на ворожій
    /// половині є фіктивним (нема ким загрожувати з структур, лишається тільки реальна
    /// presence героїв, ancient/fountain, та creep trail).
    ///
    /// Повертає множник у [0.3..1.0]:
    ///   - 1.0  — є жива ворожа вежа в радіусі ≤ <paramref name="fullRadius"/> (2500).
    ///   - 0.3  — ближча жива ворожа вежа далі ніж <paramref name="fadeRadius"/> (6000), або веж взагалі нема.
    ///   - між  — лінійна інтерполяція.
    /// Нижня межа 0.3 (не 0) бо ancient/fountain все одно небезпечні навіть без T1..T3.
    /// </summary>
    public float SampleEnemyTowerCoverage(float wx, float wy, bool playerIsRadiant,
        float fullRadius = 2500f, float fadeRadius = 6000f, float floor = 0.3f)
    {
        var enemyTeam = playerIsRadiant ? TowerTeam.Dire : TowerTeam.Radiant;
        float bestR2 = float.PositiveInfinity;
        foreach (var (team, key) in TowerMap.All())
        {
            if (team != enemyTeam) continue;
            if (!IsAlive(team, key)) continue;
            var (tx, ty) = TowerMap.GetPosition(team, key);
            float dx = wx - tx;
            float dy = wy - ty;
            float r2 = dx * dx + dy * dy;
            if (r2 < bestR2) bestR2 = r2;
        }
        if (float.IsPositiveInfinity(bestR2)) return floor;
        float r = (float)Math.Sqrt(bestR2);
        if (r <= fullRadius) return 1f;
        if (r >= fadeRadius) return floor;
        float t = (r - fullRadius) / (fadeRadius - fullRadius);
        return 1f - t * (1f - floor);
    }
}
