using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RoAura.Converters;

public sealed class ColorStringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            return (Brush)new BrushConverter().ConvertFromString(value?.ToString() ?? "#FFFFFFFF")!;
        }
        catch
        {
            return Brushes.White;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SolidColorBrush brush ? brush.Color.ToString() : "#FFFFFFFF";
}
