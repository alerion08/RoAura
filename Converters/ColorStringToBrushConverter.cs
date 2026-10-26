using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RoAura.Converters;

public sealed class ColorStringToBrushConverter : IValueConverter
{
    private static readonly BrushConverter Converter = new();
    // Frozen brushes keyed by their colour string; a frozen brush is safe to
    // share between any number of elements and threads.
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "#FFFFFFFF";
        if (Cache.TryGetValue(text, out var cached)) return cached;

        try
        {
            if (Converter.ConvertFromString(text) is not SolidColorBrush brush) return Brushes.White;
            if (brush.CanFreeze) brush.Freeze();
            Cache[text] = brush;
            return brush;
        }
        catch
        {
            return Brushes.White;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SolidColorBrush brush ? brush.Color.ToString() : "#FFFFFFFF";
}
