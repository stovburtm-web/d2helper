namespace D2Helper.Core.Models;

/// <summary>
/// Одна «крапка» ворожого юніта на minimap'і — world-координати + давність останнього
/// побачення. Свіже спостереження (stale ≈ 0) дає повний внесок у presence; з часом
/// внесок експоненційно затухає, а радіус uncertainty росте (ghost-effect).
/// </summary>
/// <param name="Wx">World X (-8288..8288).</param>
/// <param name="Wy">World Y.</param>
/// <param name="StaleSeconds">Скільки секунд тому ця точка була останньо видима (0 = зараз).</param>
/// <param name="Weight">Множник «сили» юніта. 1.0 = герой, 0.15 = lane creep, 0.3 = ranged creep тощо.</param>
public readonly record struct EnemyDot(float Wx, float Wy, float StaleSeconds, float Weight = 1f);

/// <summary>
/// Снапшот ворожих юнітів на мінімапі у конкретний момент часу. Будується адаптером
/// над <c>GameState.Minimap.Elements</c> у шарі <c>D2Helper.Core</c> і передається
/// у <see cref="DangerHeatmapRenderer"/>.
/// </summary>
public sealed class EnemyPresenceSnapshot
{
    /// <summary>Усі ворожі юніти (heroes + creeps, що ми вважаємо релевантними).</summary>
    public IReadOnlyList<EnemyDot> Dots { get; }

    /// <summary>Кількість «свіжих» (stale &lt; ~2s) ворогів — використовується як
    /// поріг довіри: при &lt;2 свіжих anti-presence не активується (мало даних).</summary>
    public int FreshCount { get; }

    /// <summary>Сума ваг усіх крапок (із врахуванням stale-decay). Знаменник для absence.</summary>
    public float TotalMass { get; }

    public EnemyPresenceSnapshot(IReadOnlyList<EnemyDot> dots)
    {
        Dots = dots;
        int fresh = 0;
        float mass = 0f;
        foreach (var d in dots)
        {
            if (d.StaleSeconds < 2f) fresh++;
            mass += StaleWeight(d.StaleSeconds) * d.Weight;
        }
        FreshCount = fresh;
        TotalMass = mass;
    }

    /// <summary>Експоненційне затухання внеску ghost-крапки: half-life ≈ 6 сек, повний нуль після ~15s.</summary>
    public static float StaleWeight(float staleSec)
    {
        if (staleSec <= 0f) return 1f;
        if (staleSec >= 15f) return 0f;
        // exp(-staleSec / 6) = 1.0 at 0s, 0.51 at 4s, 0.22 at 9s, 0.08 at 15s.
        return (float)Math.Exp(-staleSec / 6.0);
    }

    /// <summary>
    /// Локальний presence у точці (wx, wy): сума гаусіан від кожної видимої крапки.
    /// Радіус «впливу страху» збільшується зі stale (ghost розпливається).
    /// Повертає невід'ємне число (~0..1+ для типового ганк-сетапу 5 крапок).
    /// </summary>
    public float SampleLocal(float wx, float wy)
    {
        if (Dots.Count == 0) return 0f;
        float sum = 0f;
        foreach (var d in Dots)
        {
            float weight = StaleWeight(d.StaleSeconds) * d.Weight;
            if (weight <= 0f) continue;
            // Базовий радіус 2200 (~true sight). Ghost розпливається до ~3500.
            float radius = 2200f + d.StaleSeconds * 90f;
            float dx = wx - d.Wx;
            float dy = wy - d.Wy;
            float r2 = dx * dx + dy * dy;
            float sigma2 = radius * radius * 0.5f;
            sum += weight * (float)Math.Exp(-r2 / sigma2);
        }
        return sum;
    }

    /// <summary>
    /// Absence-score 0..1: «наскільки впевнено можемо сказати, що всі вороги ДАЛЕКО від цієї точки».
    /// Висока absence → ця зона зараз майже точно безпечна (хоч геометрично і червона).
    ///
    /// Логіка: якщо локальний presence &lt;&lt; глобальна total mass (всі вороги збилися деінде),
    /// absence → 1. При FreshCount &lt; 2 завжди 0 (мало даних, не довіряємо).
    /// </summary>
    public float SampleAbsence(float wx, float wy)
    {
        if (FreshCount < 2) return 0f;
        if (TotalMass < 0.5f) return 0f;
        float local = SampleLocal(wx, wy);
        // ratio: 0 = всі вороги тут, 1 = жодного тут.
        float ratio = 1f - local / TotalMass;
        if (ratio < 0f) ratio = 0f;
        if (ratio > 1f) ratio = 1f;
        // Помножимо на «впевненість» — скільки взагалі ворогів видно (≥4 → 1.0).
        float confidence = Math.Min(1f, FreshCount / 4f);
        return ratio * confidence;
    }
}
