using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace RoAura.Converters;

public sealed class Base64ToImageSourceConverter : IValueConverter
{
    // Decoded, frozen bitmaps keyed by their base64 payload. Overlay item
    // templates re-run this converter on every refresh of a bound row, so
    // decoding the same icon repeatedly was pure waste.
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();
    private const int MaxCachedImages = 512;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string encoded || string.IsNullOrWhiteSpace(encoded)) return null;
        if (Cache.TryGetValue(encoded, out var cached)) return cached;

        try
        {
            var bytes = System.Convert.FromBase64String(encoded);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // Only downscale clearly oversized user icons; DecodePixelWidth would
            // otherwise upscale the usual 24-64 px icons to 256 px in memory.
            if (bytes.Length > 128 * 1024) image.DecodePixelWidth = 256;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            if (Cache.Count >= MaxCachedImages) Cache.Clear();
            Cache[encoded] = image;
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
