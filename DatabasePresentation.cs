using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RoAura;

internal static class DatabasePresentation
{
    private static readonly ConditionalWeakTable<byte[], BitmapSource> Images = new();

    internal static void ApplyPalette(FrameworkElement target, bool dark)
    {
        ReadOnlySpan<string> keys = ["Ink", "Blue", "Muted", "Panel", "Soft", "Head", "Line", "Accent", "Row", "Field", "Frame"];
        ReadOnlySpan<uint> colors = dark
            ? [0xDBE8FAu, 0xB5D6FFu, 0xA8BDD7u, 0x1B2B40u, 0x243951u, 0x263E5Au, 0x415975u, 0x80B5F5u, 0x304C69u, 0x172437u, 0x415975u]
            : [0x153352u, 0x174E89u, 0x506884u, 0xF7FAFFu, 0xEAF2FCu, 0xDCEAFAu, 0xCBD9E9u, 0x2B61A0u, 0xE3EEFBu, 0xFFFFFFu, 0x87A7CEu];
        for (var i = 0; i < keys.Length; i++)
        {
            var rgb = colors[i];
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze();
            target.Resources["Database" + keys[i]] = brush;
        }
    }

    internal static BitmapSource? Decode(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        return Images.GetValue(bytes, data =>
        {
            using var stream = new MemoryStream(data);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        });
    }

    internal static FrameworkElement Icon(byte[]? bytes, string glyph, double size)
    {
        var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        BitmapSource? picture = null;
        try { picture = Decode(bytes); }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException) { }
        if (picture is not null)
            grid.Children.Add(new Image { Source = picture, Stretch = Stretch.Uniform });
        else
        {
            var text = new TextBlock { Text = glyph, FontSize = size > 20 ? 22 : 13,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "DatabaseMuted");
            grid.Children.Add(text);
        }
        return grid;
    }

    internal static string Count(string value) => decimal.TryParse(value.Replace(",", "").Replace(" ", ""),
        NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n.ToString("N0") :
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
