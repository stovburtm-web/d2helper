namespace D2Helper.Core.Quests;

/// <summary>
/// Чистий калькулятор балів за квест.
/// Філософія (за вимогою користувача): **тільки позитивні бали**, без штрафів.
/// Expired квест дає 0 (не -1), щоб не демотивувати гравця в реальному часі.
///
/// Tier (бажано stable, бо буде використано для майбутніх рангів):
///   Perfect = 3
///   Good    = 2
///   Min     = 1
///   None / Expired / Active = 0
///
/// Streak-бонус нараховуємо ОКРЕМО, у <see cref="StreakBonus"/>, щоб основну суму
/// було легко агрегувати по матчу/сесії/all-time.
/// </summary>
public static class ScoreCalculator
{
    /// <summary>
    /// Бали за один квест (без streak-бонусу).
    /// </summary>
    public static int Score(QuestStatus finalStatus, QuestGrade grade)
    {
        if (finalStatus != QuestStatus.Completed) return 0;
        return grade switch
        {
            QuestGrade.Perfect => 3,
            QuestGrade.Good => 2,
            QuestGrade.Min => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// Streak-бонус: за квест, що стоїть на позиції <paramref name="streakPosition"/>
    /// у поточному ланцюжку (1 = перший Completed підряд). Перші два — без бонусу,
    /// з 3-го квеста підряд +1, з 5-го +2, з 8-го +3. М'яко екпоненційно.
    /// </summary>
    public static int StreakBonus(int streakPosition) => streakPosition switch
    {
        >= 8 => 3,
        >= 5 => 2,
        >= 3 => 1,
        _ => 0,
    };

    /// <summary>
    /// Повна сума балів за квест: <see cref="Score"/> + <see cref="StreakBonus"/>.
    /// </summary>
    public static int Total(QuestStatus finalStatus, QuestGrade grade, int streakPosition)
        => Score(finalStatus, grade) + (finalStatus == QuestStatus.Completed ? StreakBonus(streakPosition) : 0);
}
