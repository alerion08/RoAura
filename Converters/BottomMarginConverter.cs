using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RoAura.Converters;

public sealed class BottomMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(0, 0, 0, value is double gap ? Math.Max(0, gap) : 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
