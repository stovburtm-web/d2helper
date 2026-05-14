using System.Globalization;
using Avalonia.Data.Converters;
using D2Helper.Core.Quests;

namespace D2Helper.UI.ViewModels;

/// <summary>
/// Конвертер для StyleClasses у XAML: повертає true, якщо значення
/// дорівнює заздалегідь заданому статусу. Використовується для
/// підсвічування рядків квестів кольором.
/// </summary>
public sealed class StatusEqualsConverter : IValueConverter
{
    public static readonly StatusEqualsConverter Active = new(QuestStatus.Active);
    public static readonly StatusEqualsConverter Completed = new(QuestStatus.Completed);
    public static readonly StatusEqualsConverter Expired = new(QuestStatus.Expired);
    public static readonly StatusEqualsConverter Pending = new(QuestStatus.Pending);

    private readonly QuestStatus _target;
    private StatusEqualsConverter(QuestStatus target) { _target = target; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is QuestStatus s && s == _target;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
