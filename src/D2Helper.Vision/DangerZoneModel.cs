namespace D2Helper.Vision;

/// <summary>Сторона за яку грає гравець — задає орієнтацію safe→danger вектора.</summary>
public enum PlayerSide
{
    Radiant,
    Dire,
}

/// <summary>
/// V1.1 геометрична модель небезпеки на мапі Dota 2.
///
/// Не використовує комп'ютерний зір. Бере на вхід:
///   - сторону гравця (Radiant/Dire),
///   - <c>gameTime</c> в секундах (з GSI <c>Map.GameTime</c> або <c>ClockTime</c>),
/// і повертає для будь-якої world-координати число 0..1, де
///   - <b>0.0</b> — повністю безпечно (свій фонтан, своя база),
///   - <b>0.5</b> — лінія Шепарда (контестед мід-лейн),
///   - <b>1.0</b> — повністю небезпечно (ворожий фонтан).
///
/// Модель груба і не враховує статуси веж (Dota2GSI 2.1.1 не експонує <c>buildings</c>),
/// але апроксимує його через <c>gameTime</c>:
///   - 0..6 хв (лайн-фаза): своя половина дуже безпечна, ворожа — дуже небезпечна.
///   - 6..14 хв (transition): межа safe→danger трохи зрушується назад до своєї бази.
///   - 14..25 хв: ще зрушується.
///   - 25+ хв (late): безпечно тільки прямо біля свого фонтану.
/// </summary>
public static class DangerZoneModel
{
    // Фонтани Dota (приблизні world-координати).
    private const float RadiantFountainX = -7000f;
    private const float RadiantFountainY = -7000f;
    private const float DireFountainX = 7000f;
    private const float DireFountainY = 7000f;

    /// <summary>Розраховує danger ∈ [0..1] для світової точки.</summary>
    /// <param name="wx">World X (-8288..8288).</param>
    /// <param name="wy">World Y (-8288..8288).</param>
    /// <param name="side">Сторона гравця.</param>
    /// <param name="gameTime">Час гри в секундах (Map.GameTime). Може бути від'ємним до 0:00.</param>
    public static float ComputeDanger(float wx, float wy, PlayerSide side, float gameTime)
    {
        // 1. Проекція на вісь "свій фонтан → ворожий фонтан".
        // Це фактично нормована величина (wx+wy) з різним знаком для Radiant/Dire.
        // Для Radiant вісь напрямлена з (-7000,-7000) до (7000,7000), отже proj = (wx+wy)/(2*7000) ∈ [-1..1].
        float proj = (side == PlayerSide.Radiant)
            ? (wx + wy) / 14000f
            : -(wx + wy) / 14000f;
        if (proj < -1f) proj = -1f;
        if (proj > 1f) proj = 1f;

        // 2. Базовий score: переводимо проекцію в [0..1] де 0 — свій фонтан.
        float baseScore = (proj + 1f) * 0.5f;

        // 3. Фаза гри — зрушує "нейтральну" точку 0.5 ближче до своєї сторони (тобто
        //    збільшує danger на однаковій position з часом).
        float phaseShift = ComputePhaseShift(gameTime);
        float score = baseScore + phaseShift;

        // 4. М'якший фінальний clamp + легка сигмоїда щоб межа була чіткішою для рендеру.
        if (score < 0f) score = 0f;
        if (score > 1f) score = 1f;
        // Sigmoid (k=4) робить перехід посередині різкішим, але всередині зон лишається плавним.
        score = Sigmoid(score, k: 4f);
        return score;
    }

    /// <summary>Зрушення нейтральної точки залежно від часу гри (нелінійне).</summary>
    private static float ComputePhaseShift(float gameTime)
    {
        // Шкала зрушень — підібрано емпірично:
        //   pregame/0..6 хв: -0.03 → 0   (своя half безпечна, АЛЕ річка/підступи лишаються
        //                                  contested через runes 2:00/4:00 і смок-ганки з мiд'у)
        //   6..14 хв        : 0  → +0.05 (T1 контестед, межа на лінії шепарда)
        //   14..25 хв       : +0.05 → +0.15 (T2 контестед, ворог глибше)
        //   25..40 хв       : +0.15 → +0.22 (late, тільки база безпечна)
        //   40+ хв          : 0.22 → 0.28
        if (gameTime < 360f)   return Lerp(-0.03f,  0.00f, gameTime / 360f);
        if (gameTime < 840f)   return Lerp( 0.00f,  0.05f, (gameTime - 360f) / 480f);
        if (gameTime < 1500f)  return Lerp( 0.05f,  0.15f, (gameTime - 840f) / 660f);
        if (gameTime < 2400f)  return Lerp( 0.15f,  0.22f, (gameTime - 1500f) / 900f);
        return 0.28f;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Розширений варіант: накладає на геометричний бейс динамічні модифікатори:
    /// fog density (з захопленої мінімапи) і halo навколо власного героя.
    /// </summary>
    /// <param name="wx">World X.</param>
    /// <param name="wy">World Y.</param>
    /// <param name="side">Сторона гравця.</param>
    /// <param name="gameTime">Час гри, сек.</param>
    /// <param name="fogDensity">0..1 щільність туману в цій точці (0 = добре видно, 1 = повна темрява).
    /// 0.5 = немає сигналу — модифікатор нульовий.</param>
    /// <param name="heroX">World X героя (опц.).</param>
    /// <param name="heroY">World Y героя (опц.).</param>
    /// <param name="heroHaloRadius">Радіус ефекту героя в world-units. За дефолтом ~true sight range.</param>
    /// <param name="fogWeight">Вага fog-модифікатора (макс. ±половина від цього).</param>
    /// <param name="heroHaloWeight">Макс зниження danger біля героя.</param>
    /// <param name="empiricalDensity">Empirical death density [0..1] з <see cref="EmpiricalDeathField"/>.
    /// <c>NaN</c> = немає сигналу (немає bin'а / поза картою). Дефолт NaN.</param>
    /// <param name="empiricalWeight">Вага empirical-density: при 1.0 повністю переписує danger вгору
    /// у точках з частими смертями. Дефолт 0.35 — м'який nudge поверх геометрії.</param>
    /// <param name="presenceLocal">V1.3: локальна щільність ворожих юнітів навколо точки (0..~2+).
    /// 0 = жодного ворога поряд; ~1 = один ворог точно тут; &gt;1 = кластер. <c>NaN</c> = немає даних.</param>
    /// <param name="presenceWeight">Множник для presence-nudge: при 1.0 один ворог поряд додає ~0.6 danger.</param>
    /// <param name="absenceScore">V1.3: 0..1, наскільки впевнено всі вороги ДАЛЕКО (anti-presence).
    /// При absence=1 і aggressive ваги — точка стає на ~75% безпечнішою. <c>NaN</c> = вимкнено.</param>
    /// <param name="absenceWeight">Aggressive за дефолтом (0.75): сильно віримо minimap'у.</param>
    /// <param name="friendlyControl">V1.4: локальна присутність союзних героїв і лайн-крепів (0..1.5+).
    /// Контр-балансує загрозу: є з ким приймати файт → danger знижується. <c>NaN</c> = вимкнено.</param>
    /// <param name="friendlyControlWeight">Множник: 4 союзники поряд (fc≈1.0) → -0.4 danger.</param>
    /// <param name="towerAuraLocal">V1.7: signed aura живих веж у цій точці (з <c>TowerSnapshot.SampleAura</c>).
    /// Додатне значення = поряд живі ворожі вежі (підвищує danger), від'ємне = свої вежі (safety).
    /// <c>NaN</c> = немає даних про вежі.</param>
    /// <param name="towerAuraWeight">Множник tower-aura: при 1.0 повна aura T3 переб'є absence-crush навіть на 25-й хв.</param>
    /// <param name="enemyCreepHint">V1.8: локальний "creep hint" — сума видимих ворожих лейн-крипів у радіусі ~900.
    /// Сам по собі НЕ змінює danger; разом з низьким <paramref name="presenceLocal"/> = слабкий
    /// safety nudge ("вороги десь не на цьому лайні"). <c>NaN</c> або 0 = немає сигналу.</param>
    /// <param name="enemyCreepHintWeight">Макс зниження danger від creep hint без героїв (за дефолтом -0.12).</param>
    public static float ComputeDangerDynamic(
        float wx, float wy, PlayerSide side, float gameTime,
        float fogDensity = 0.5f,
        float? heroX = null, float? heroY = null,
        float heroHaloRadius = 2200f,
        float fogWeight = 0.35f,
        float heroHaloWeight = 0.40f,
        float empiricalDensity = float.NaN,
        float empiricalWeight = 0.35f,
        float presenceLocal = float.NaN,
        float presenceWeight = 0.85f,
        float absenceScore = float.NaN,
        float absenceWeight = 0.75f,
        float friendlyControl = float.NaN,
        float friendlyControlWeight = 0.50f,
        float towerAuraLocal = float.NaN,
        float towerAuraWeight = 0.50f,
        float enemyCreepHint = float.NaN,
        float enemyCreepHintWeight = 0.12f)
    {
        float baseDanger = ComputeDanger(wx, wy, side, gameTime);
        float danger = baseDanger;

        // Fog modifier — асиметричний:
        //   - темніше за нейтраль (fogDensity > 0.5): ЗАВЖДИ додає danger (ховається hero/гланк).
        //   - світліше за нейтраль (fogDensity < 0.5): зменшує danger ТІЛЬКИ у своїй/нейтральній
        //     зоні (base < 0.55). На ворожому highground видимість через варди не робить точку
        //     "безпечною" — туди все одно небезпечно лізти.
        float fogDelta = (fogDensity - 0.5f) * 2f * fogWeight;
        if (fogDelta > 0f)
        {
            danger += fogDelta;
        }
        else if (danger < 0.55f)
        {
            danger += fogDelta; // зменшення тільки у своїй половині
        }

        // V1.7.2: hero-halo прибрано. Сама присутність героя на точці ≠ безпека —
        // "де я зараз стою" не повинно зеленити навколо. Підказку про safety має давати
        // лише комбінація tower-aura (своя вежа поряд) + friendlyControl (2+ союзники).
        _ = heroX; _ = heroY; _ = heroHaloRadius; _ = heroHaloWeight; // unused, params kept for API compat

        // Empirical death density — асиметричний nudge:
        //   - відсутність зафіксованих смертей у вибірці != безпека (вибірка ~1k матчів).
        //     Тому empirical може ТІЛЬКИ збільшити danger, ніколи не знизити.
        //   - формула: blended = geom*(1-w) + emp*w; беремо max(danger, blended).
        //   - V1.5.4: на ранній грі (0..5min) геометрична модель ще "пласка" (phaseShift майже 0),
        //     тому покладаємось на empirical сильніше. Вага лінійно спадає від 2x до 1x на 0..600s.
        if (!float.IsNaN(empiricalDensity) && empiricalDensity > 0f)
        {
            float effectiveEmpiricalWeight = empiricalWeight;
            if (gameTime < 600f)
            {
                float earlyBoost = 1f - MathF.Max(0f, gameTime) / 600f; // 1 на t=0, 0 на t=600
                effectiveEmpiricalWeight = empiricalWeight * (1f + earlyBoost); // 2x → 1x
                if (effectiveEmpiricalWeight > 0.95f) effectiveEmpiricalWeight = 0.95f;
            }
            float blended = danger * (1f - effectiveEmpiricalWeight) + empiricalDensity * effectiveEmpiricalWeight;
            if (blended > danger) danger = blended;
        }

        // V1.3: minimap presence — реальні позиції видимих ворогів.
        // Логіка симетрична до geometric model:
        //   - presenceLocal > 0 → ворог тут і зараз → DANGER += k_pres * min(1, presence).
        //     Це переб'є будь-який «зелений geometric» — якщо ворог стоїть у твоїй базі, це не safe.
        //   - absenceScore > 0 → вороги КУПКОЮ деінде → danger *= (1 - k_abs * absence).
        //     Це opens сейф-зони у «червоному geometric» (твій own jungle коли пачка пушить мід).
        //   - Порядок важливий: спершу +presence (можемо зробити локально гарячим), потім -absence
        //     (масштабуємо решту карти вниз). Локально-гаряча точка зберігає високий danger.
        if (!float.IsNaN(presenceLocal) && presenceLocal > 0f)
        {
            float p = presenceLocal;
            if (p > 1.5f) p = 1.5f; // soft cap — кластер з 5 не може зробити >2x
            danger += p * presenceWeight;
        }
        if (!float.IsNaN(absenceScore) && absenceScore > 0f)
        {
            // V1.6: absence-crush діє ВСЮДИ без gate'ів. Завдяки V1.6 EnemyPresenceSnapshot,
            // passive вороги (у фонтані) пушать presenceLocal на своїй позиції → ratio там
            // близький до 0 → absence автоматично 0 на фонтані. Якщо вороги дійсно деінде —
            // решта мапи (mid, jungle, ворожі лайни поза фонтаном) природно крушиться.
            danger *= 1f - absenceScore * absenceWeight;

            // V1.6.1: ворожа зона (base ≥ 0.5) має tower aggression — не можна крушити в зелене.
            // Floor лінійно слабшає з ходом гри: на early game (всі вежі стоять) — 80% від base,
            // на late game (T1/T2 падають, push deep можливий) — 30% від base.
            if (baseDanger >= 0.5f)
            {
                float t;
                if (gameTime < 360f)      t = 0f;                                   // 0..6хв: floor=80%
                else if (gameTime < 1500f) t = (gameTime - 360f) / 1140f;           // 6..25хв: лінійно
                else                      t = 1f;                                   // 25+хв: floor=30%
                float floorWeight = 0.80f + (0.30f - 0.80f) * t;                    // 0.80 → 0.30
                float floor = baseDanger * floorWeight;
                if (danger < floor) danger = floor;
            }
        }

        // V1.7.2: friendly control працює ТІЛЬКИ якщо реально 2+ союзників поряд
        // І точка вже у contested/danger зоні (base ≥ 0.38). "Я сам стою на лайні" ≠ safety.
        // Один союзний герой на власній точці дає SampleLocal ≈ 1.0; пара героїв на тій же точці ~1.5.
        // Тому поріг fc > 1.2 — це гарантує що поряд є хоча б 1 додатковий ally, окрім тебе самого.
        if (!float.IsNaN(friendlyControl) && friendlyControl > 1.2f && baseDanger >= 0.38f)
        {
            float fc = friendlyControl - 1.0f; // знімаємо внесок самого гравця
            if (fc > 1.5f) fc = 1.5f;
            danger -= fc * friendlyControlWeight;
        }

        // V1.7: tower aura — справжні живі вежі переб'ють absence-crush у своїх зонах.
        // - Поряд жива ворожа вежа → +danger (creep aggression, gank вертушка з-під вежі).
        // - Поряд жива своя вежа → -danger (safety, відступ).
        // Мертві вежі не дають внеску → коли впаде ворожий Т1, та зона природно стає
        // прохідною для пуша, не потребуючи штучних gate'ів за gameTime.
        //
        // V1.7.4: friendly aura (negative) НЕ застосовується якщо ворожі герої видимі поряд
        // (presenceLocal ≥ 0.3) АБО точка вже у ворожій зоні (base ≥ 0.55). Бо «стояти під своєю
        // вежею» ≠ безпека, коли там фізично стоять вороги. Ворожа aura (positive) додається завжди.
        if (!float.IsNaN(towerAuraLocal) && towerAuraLocal != 0f)
        {
            if (towerAuraLocal > 0f)
            {
                danger += towerAuraLocal * towerAuraWeight;
            }
            else
            {
                bool enemyNearby = !float.IsNaN(presenceLocal) && presenceLocal >= 0.2f;
                bool enemyHalf = baseDanger >= 0.55f;
                if (!enemyNearby && !enemyHalf)
                {
                    danger += towerAuraLocal * towerAuraWeight; // negative → reduce
                }
            }
        }

        // V1.8: enemy creep hint — видимі ворожі лейн-крипи на цьому тіку. Сам по собі НЕ
        // загроза, а ІНФОРМАЦІЯ: "тут зараз бачимо ворожих крипів". У поєднанні з низьким
        // presenceLocal (немає видимих ворожих героїв поряд) це означає "вороги десь не на
        // цьому лайні" — ймовірно smoke gank/rosh/torm деінде. Локально це м'який safety nudge:
        // можна підійти до краю хвилі і пофармити, але глибше за неї — не йти.
        //
        // Gate'и:
        //   1) creepHint > 0.3  — реально бачимо хвилю, не випадковий крип.
        //   2) presenceLocal < 0.2 — поряд немає ворожих героїв (інакше це активний laning).
        //   3) baseDanger < 0.70 — не в глибокій ворожій зоні (під T3 не зеленимо ніколи).
        //
        // Влив: до -enemyCreepHintWeight (за дефолтом -0.12) у точці кластера крипів,
        // спадає з гаусіаною до 0 на ~900 unit (як SampleCreepLocal).
        if (!float.IsNaN(enemyCreepHint) && enemyCreepHint > 0.3f && baseDanger < 0.70f)
        {
            bool enemyHeroNearby = !float.IsNaN(presenceLocal) && presenceLocal >= 0.2f;
            if (!enemyHeroNearby)
            {
                float hint = enemyCreepHint;
                if (hint > 1.5f) hint = 1.5f;
                danger -= hint * enemyCreepHintWeight;
            }
        }

        if (danger < 0f) danger = 0f;
        if (danger > 1f) danger = 1f;
        return danger;
    }

    /// <summary>Логістика навколо 0.5 — робить перехід різкішим, не торкаючись країв 0/1.</summary>
    private static float Sigmoid(float x, float k)
    {
        // 1 / (1 + e^(-k*(x-0.5)))
        var z = -k * (x - 0.5f);
        return 1f / (1f + (float)Math.Exp(z));
    }
}
