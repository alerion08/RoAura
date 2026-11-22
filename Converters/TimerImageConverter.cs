using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace RoAura.Converters;

public sealed class TimerImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is byte[] { Length: > 0 } data)
            {
                using var stream = new MemoryStream(data, writable: false);
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            if (value is string { Length: > 0 } path && File.Exists(path))
                return new BitmapImage(new Uri(Path.GetFullPath(path)));
        }
        catch (Exception ex) when (ex is IOException or FormatException or NotSupportedException)
        {
            Services.DebugLog.Write($"TIMER IMAGE: {ex.Message}");
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
