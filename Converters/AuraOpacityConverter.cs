using System.Globalization;
using System.Windows.Data;

namespace RoAura.Converters;

public sealed class AuraOpacityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var configuredOpacity = values.Length > 0 && values[0] is double opacity
            ? Math.Clamp(opacity, 0.2, 1)
            : 1;
        var isReady = values.Length > 1 && values[1] is bool ready && ready;

        return configuredOpacity * (isReady ? 1 : 0.68);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
