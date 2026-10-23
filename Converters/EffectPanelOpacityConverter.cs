using System.Globalization;
using System.Windows.Data;

namespace RoAura.Converters;

public sealed class EffectPanelOpacityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var active = values.Length > 0 && values[0] is true;
        var activeOpacity = values.Length > 1 && values[1] is double activeValue ? activeValue : 100;
        var inactiveOpacity = values.Length > 2 && values[2] is double inactiveValue ? inactiveValue : 42;
        return Math.Clamp((active ? activeOpacity : inactiveOpacity) / 100d, 0, 1);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
