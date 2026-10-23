using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RoAura.Converters;

public sealed class DoubleToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var padding = value is double number && double.IsFinite(number)
            ? Math.Max(0, number)
            : 0;
        return new Thickness(padding);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Thickness thickness ? thickness.Left : 0d;
}
