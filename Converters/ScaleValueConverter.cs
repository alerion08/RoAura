using System.Globalization;
using System.Windows.Data;

namespace RoAura.Converters;

public sealed class ScaleValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var size = value is double number ? number : 72;
        if (!double.TryParse(parameter?.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var factor))
            factor = 1;

        return Math.Max(10, size * factor);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
