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
/// <param name="IsPassive">V1.5: <c>true</c> якщо юніт зараз не є активною загрозою (в фонтані /
/// respawning). Такі крапки НЕ пушать червоним в <see cref="EnemyPresenceSnapshot.SampleLocal"/>,
/// але АКТИВНО враховуються в absence як fresh visible (FreshCount + TotalMass), що дає
/// бажаний ефект «всі вороги в таверні → решта мапи зелена».</param>
public readonly record struct EnemyDot(float Wx, float Wy, float StaleSeconds, float Weight = 1f, bool IsPassive = false);

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

    /// <summary>V1.5: скільки ворожих героїв зараз живі (5 = всі живі). Оновлюється трекером
    /// через <c>PlayerDeathsChanged</c> + самовідновлення за приблизний respawn timer.
    /// Використовується в <see cref="SampleAbsence"/> для динамічного знаменника confidence.</summary>
    public int AliveEnemyCount { get; init; } = 5;

    /// <summary>V1.8: видимі ворожі лейн-крипи у поточний тік. БЕЗ ghost-decay (крипи рухаються
    /// швидко, історія неактуальна) — щотіка перебудовується з GSI. Використовується як
    /// «safety hint»: якщо крипи видимі, а ворожих героїв поряд немає, ця ділянка ймовірно
    /// pacified (вороги десь не на цьому лайні).</summary>
    public IReadOnlyList<EnemyDot> CreepDots { get; init; } = Array.Empty<EnemyDot>();

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
            // V1.6: passive (fountain) крапки ТЕЖ пушать presence на своїй позиції.
            // Це тримає ворожий фонтан "червоним" природно (без штучних gate'ів).
            // Поза фонтаном їхній внесок гасне за гаусом — й absence-логіка
            // у DangerZoneModel чисто зменшує danger у віддалених порожніх зонах.
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
        // Помножимо на «впевненість» — скільки взагалі ворогів видно відносно живих.
        // V1.5: динамічний знаменник — якщо 2 вороги померли (aliveCount=3), то бачити 3 живих
        // = повна впевненість. Раніше знаменник був рівним 4 (3/4 = 0.75 conf).
        int denom = Math.Max(1, AliveEnemyCount - 1);
        float confidence = Math.Min(1f, FreshCount / (float)denom);
        return ratio * confidence;
    }

    /// <summary>
    /// V1.8: локальний "creep hint" у точці (wx, wy) — сума гаусіан від видимих ворожих
    /// лейн-крипів. Радіус ~900 unit (приблизний радіус хвилі). Не залежить від stale,
    /// бо крипи в <see cref="CreepDots"/> завжди свіжі.
    ///
    /// Семантика інтерпретації — у <c>DangerZoneModel</c>: якщо creep hint &gt; 0 І
    /// presenceLocal малий (немає ворожих героїв поряд) → ділянка "м'яко зеленіє" як
    /// safety signal ("вороги десь не на цьому лайні, можу пофармити поруч").
    /// </summary>
    public float SampleCreepLocal(float wx, float wy)
    {
        if (CreepDots.Count == 0) return 0f;
        float sum = 0f;
        const float radius = 900f;
        const float sigma2 = radius * radius * 0.5f;
        foreach (var d in CreepDots)
        {
            float dx = wx - d.Wx;
            float dy = wy - d.Wy;
            float r2 = dx * dx + dy * dy;
            if (r2 > sigma2 * 9f) continue; // cutoff на ~3σ
            sum += d.Weight * (float)Math.Exp(-r2 / sigma2);
        }
        return sum;
    }

    /// <summary>
    /// V1.8.1: directional «creep trail» — лейн-equilibrium-сигнал.
    /// Для кожного ворожого крипа будуємо конус, що тягнеться ВІД крипа В НАПРЯМКУ ворожого
    /// фонтану. Точки в цьому конусі (тобто «за хвилею з точки зору гравця, ближче до бази
    /// ворога») отримують позитивний внесок — там може фармити ворожий керрі а саппорти
    /// чатують у джунглях. Точки «позаду крипа» (між крипом і нашим фонтаном) НЕ отримують
    /// внеску (їх покриває <see cref="SampleCreepLocal"/> як safety hint).
    ///
    /// Параметри: forward window 200..6000 unit (не до самої бази, щоб не перебивати T3 aura),
    /// бокова ширина σ=2000 (приблизно лейн + прилеглий ліс).
    /// </summary>
    /// <param name="wx">Sample world X.</param>
    /// <param name="wy">Sample world Y.</param>
    /// <param name="enemyFountainX">World X ворожого фонтану (≈ ±7000).</param>
    /// <param name="enemyFountainY">World Y ворожого фонтану (≈ ±7000).</param>
    /// <param name="ownFountainX">World X нашого фонтану (для нормалізації напрямку).</param>
    /// <param name="ownFountainY">World Y нашого фонтану.</param>
    public float SampleCreepBeyond(float wx, float wy,
        float enemyFountainX, float enemyFountainY,
        float ownFountainX, float ownFountainY)
    {
        if (CreepDots.Count == 0) return 0f;
        // Напрямок «уперед» = від нашого фонтану до ворожого (нормалізований).
        float fdx = enemyFountainX - ownFountainX;
        float fdy = enemyFountainY - ownFountainY;
        float flen = (float)Math.Sqrt(fdx * fdx + fdy * fdy);
        if (flen < 1f) return 0f;
        float fx = fdx / flen;
        float fy = fdy / flen;

        const float forwardMin = 200f;
        const float forwardMax = 6000f;
        const float lateralSigma = 2000f;
        const float lateralSigma2 = lateralSigma * lateralSigma * 0.5f;

        float sum = 0f;
        foreach (var d in CreepDots)
        {
            float dx = wx - d.Wx;
            float dy = wy - d.Wy;
            // Проєкція на напрямок «уперед» (forward) і перпендикуляр (lateral).
            float forward = dx * fx + dy * fy;
            if (forward < forwardMin || forward > forwardMax) continue;
            float latX = dx - forward * fx;
            float latY = dy - forward * fy;
            float lat2 = latX * latX + latY * latY;
            if (lat2 > lateralSigma2 * 9f) continue;

            float lateralFalloff = (float)Math.Exp(-lat2 / lateralSigma2);
            // Forward falloff: плавно наростає 200→1500, тримається до 4500, спадає до 6000.
            float forwardFalloff;
            if (forward < 1500f) forwardFalloff = (forward - forwardMin) / (1500f - forwardMin);
            else if (forward > 4500f) forwardFalloff = 1f - (forward - 4500f) / (forwardMax - 4500f);
            else forwardFalloff = 1f;
            if (forwardFalloff < 0f) forwardFalloff = 0f;

            sum += d.Weight * lateralFalloff * forwardFalloff;
        }
        return sum;
    }
}
