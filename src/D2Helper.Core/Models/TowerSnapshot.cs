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
}
