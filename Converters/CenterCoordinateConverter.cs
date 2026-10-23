using System.Globalization;
using System.Windows.Data;

namespace RoAura.Converters;

public sealed class CenterCoordinateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double coordinate || values[1] is not double size)
            return 0.0;

        return coordinate - size / 2.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
