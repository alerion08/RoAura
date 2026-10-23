using System.Globalization;
using System.Windows.Data;

namespace RoAura.Converters;

public sealed class ProgressToHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var progress = values.Length > 0 && values[0] is double number
            ? Math.Clamp(number, 0, 100)
            : 0;
        var maximumHeight = values.Length > 1 && values[1] is double size
            ? Math.Clamp(size, 40, 128)
            : 72;

        return maximumHeight * progress / 100;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
