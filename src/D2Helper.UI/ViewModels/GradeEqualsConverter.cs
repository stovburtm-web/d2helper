using System.Globalization;
using Avalonia.Data.Converters;
using D2Helper.Core.Quests;

namespace D2Helper.UI.ViewModels;

/// <summary>
/// Конвертер для StyleClasses у XAML: true, якщо QuestGrade співпадає з заданим.
/// Використовується щоб розфарбувати виконані квести за рівнем виконання
/// (Min/Good/Perfect).
/// </summary>
public sealed class GradeEqualsConverter : IValueConverter
{
    public static readonly GradeEqualsConverter Min = new(QuestGrade.Min);
    public static readonly GradeEqualsConverter Good = new(QuestGrade.Good);
    public static readonly GradeEqualsConverter Perfect = new(QuestGrade.Perfect);

    private readonly QuestGrade _target;
    private GradeEqualsConverter(QuestGrade target) { _target = target; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is QuestGrade g && g == _target;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
