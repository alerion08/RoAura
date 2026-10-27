using System.Globalization;
using System.Windows.Data;

namespace RoAura.Converters;

public sealed class PercentToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double percent ? Math.Clamp(percent / 100d, 0, 1) : 1d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double opacity ? Math.Clamp(opacity * 100d, 0, 100) : 100d;
}
