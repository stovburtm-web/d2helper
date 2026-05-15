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
        //   pregame/0..6 хв: -0.08 (своя сторона ще безпечніша)
        //   6..14 хв        : 0  → +0.05 (T1 контестед, межа на лінії шепарда)
        //   14..25 хв       : +0.05 → +0.15 (T2 контестед, ворог глибше)
        //   25..40 хв       : +0.15 → +0.22 (late, тільки база безпечна)
        //   40+ хв          : 0.22 → 0.28
        if (gameTime < 360f)   return Lerp(-0.10f, -0.05f, gameTime / 360f);
        if (gameTime < 840f)   return Lerp(-0.05f,  0.05f, (gameTime - 360f) / 480f);
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
    public static float ComputeDangerDynamic(
        float wx, float wy, PlayerSide side, float gameTime,
        float fogDensity = 0.5f,
        float? heroX = null, float? heroY = null,
        float heroHaloRadius = 2200f,
        float fogWeight = 0.35f,
        float heroHaloWeight = 0.40f,
        float empiricalDensity = float.NaN,
        float empiricalWeight = 0.35f)
    {
        float danger = ComputeDanger(wx, wy, side, gameTime);

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

        // Halo навколо героя: лінійне затухання від центру до радіуса.
        // Активний ТІЛЬКИ у своїй/нейтральній зоні (base < 0.55). На ворожому highground
        // присутність героя не "розсвічує" точку — там і має лишатись червоне попередження.
        if (heroX is float hx && heroY is float hy && danger < 0.55f)
        {
            float dx = wx - hx;
            float dy = wy - hy;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < heroHaloRadius)
            {
                float halo = 1f - dist / heroHaloRadius;
                // Сглажуємо щоб не було різкого кола.
                halo = halo * halo;
                danger -= halo * heroHaloWeight;
            }
        }

        // Empirical death density — асиметричний nudge:
        //   - відсутність зафіксованих смертей у вибірці != безпека (вибірка ~1k матчів).
        //     Тому empirical може ТІЛЬКИ збільшити danger, ніколи не знизити.
        //   - формула: blended = geom*(1-w) + emp*w; беремо max(danger, blended).
        if (!float.IsNaN(empiricalDensity) && empiricalDensity > 0f)
        {
            float blended = danger * (1f - empiricalWeight) + empiricalDensity * empiricalWeight;
            if (blended > danger) danger = blended;
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
