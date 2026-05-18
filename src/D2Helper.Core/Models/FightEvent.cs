namespace D2Helper.Core.Models;

/// <summary>
/// Рівень серйозності подій файту.
/// </summary>
public enum FightSeverity
{
    /// <summary>Стичка — 1 алі під атакою з помірним Δhp (≥12%/2с). Жовтий пульс, без звуку.</summary>
    Skirmish = 1,

    /// <summary>Teamfight — burst (≥25%/2с або ≥18%/1с) АБО ≥2 алі під атакою АБО смерть алі.
    /// Червоний flash + ping-звук.</summary>
    Teamfight = 2,
}

/// <summary>
/// Подія "на цій точці карти прямо зараз щось важливе" — щоб overlay міг блимнути
/// пульсом на мінімапі й (для Teamfight) дати ping-звук. Видається
/// <see cref="Gsi.FightDetector"/> на основі рапіного Δhp/Δmp по союзних героях.
/// </summary>
/// <param name="EventTime">Момент виявлення (від <c>DateTime.UtcNow</c> у проді).</param>
/// <param name="Wx">World X точки де треба сигналити (= позиція "епіцентру", типово ally hero).</param>
/// <param name="Wy">World Y.</param>
/// <param name="Severity">Skirmish / Teamfight — визначає колір + наявність звуку.</param>
/// <param name="InvolvedAllyIds">PlayerID союзників, які потрапили в подію. Може бути 1..5.</param>
/// <param name="Reason">Короткий human-readable код (наприклад "burst", "cluster", "death") — для логів/дебагу.</param>
public sealed record FightEvent(
    DateTime EventTime,
    float Wx,
    float Wy,
    FightSeverity Severity,
    IReadOnlyList<int> InvolvedAllyIds,
    string Reason);
