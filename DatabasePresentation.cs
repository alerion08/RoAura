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

    internal static void SetActionStyle(Button button, bool danger = false)
    {
        button.SetResourceReference(FrameworkElement.StyleProperty, danger ? "DatabaseDangerButton" : "DatabasePrimaryButton");
        if (button.Content is string caption)
            button.Content = new TextBlock { Text = caption, Foreground = Brushes.White };
    }

    internal static void AddDialogStyles(Window dialog)
    {
        dialog.FontFamily = new FontFamily("Segoe UI");
        dialog.FontSize = 13;
        dialog.SetResourceReference(Control.ForegroundProperty, "DatabaseInk");
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/RoAura;component/Themes/DatabasePresentation.xaml", UriKind.Relative)
        });
    }

    internal static string Count(string value) => decimal.TryParse(value.Replace(",", "").Replace(" ", ""),
        NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n.ToString("N0") :
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
