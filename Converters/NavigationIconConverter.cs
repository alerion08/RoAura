using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RoAura.Converters;

public sealed class NavigationIconConverter : IMultiValueConverter
{
    private static readonly Dictionary<string, ImageSource> Cache = [];
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string path) return DependencyProperty.UnsetValue;
        var enabled = values[1] is true;
        var key = path + enabled;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var bitmap = new BitmapImage(new Uri("pack://application:,,,/RoAura;component/" + path));
        ImageSource image = enabled ? bitmap : ToGrayscale(bitmap);
        image.Freeze(); Cache[key] = image;
        return image;
    }
    public static BitmapSource ToGrayscale(BitmapSource source)
    {
        var rgba = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = rgba.PixelWidth * 4;
        var pixels = new byte[stride * rgba.PixelHeight];
        rgba.CopyPixels(pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var gray = (byte)((29 * pixels[i] + 150 * pixels[i + 1] + 77 * pixels[i + 2] + 128) >> 8);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = gray;
        }
        var result = BitmapSource.Create(rgba.PixelWidth, rgba.PixelHeight, rgba.DpiX, rgba.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
