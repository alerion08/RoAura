using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace RoAura.Converters;

public sealed class Base64ToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string encoded || string.IsNullOrWhiteSpace(encoded)) return null;

        try
        {
            var bytes = System.Convert.FromBase64String(encoded);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
