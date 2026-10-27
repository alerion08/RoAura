using System.Globalization;
using System.Windows.Data;
using RoAura.Services;

namespace RoAura.Converters;

/// <summary>Formats a Gold Goblin overcharge level as "level — percent%".</summary>
public sealed class OverchargeLevelLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int level) return value?.ToString() ?? "";
        var percent = GoldGoblinService.ApplyOvercharge(100, level) - 100;
        return $"{level} — {percent}%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
