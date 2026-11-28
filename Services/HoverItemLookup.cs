using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoAura.Models;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace RoAura.Services;

internal enum HoverItemAction { Catalog, Selling, Buying }

internal static class HoverItemLookup
{
    private static readonly Regex Quantity = new(@"\s*:\s*[\d,.]+\s*(?:ea\.?|pcs\.?|шт\.?)?\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex Refine = new(@"^\+\s*\d+\s+", RegexOptions.None);

    internal static DatabaseItem[] Match(string text, IReadOnlyList<DatabaseItem> items)
    {
        var name = Quantity.Replace(text.Trim(), "");
        var exact = Exact(name);
        if (exact.Length != 0) return exact;
        name = Refine.Replace(name, "");
        exact = Exact(name);
        if (exact.Length != 0) return exact;

        var candidates = items.Where(item =>
        {
            var suffix = item.Name.Trim();
            if (suffix.Length == 0 || name.Length <= suffix.Length ||
                !name.EndsWith(" " + suffix, StringComparison.OrdinalIgnoreCase)) return false;
            var prefix = name[..^(suffix.Length + 1)];
            return prefix.Count(c => c == ' ') < 3 && prefix.All(c => char.IsLetter(c) || c is ' ' or '\'' or '’' or '-');
        }).ToArray();
        if (candidates.Length == 0) return [];
        var longest = candidates.Max(item => item.Name.Trim().Length);
        return candidates.Where(item => item.Name.Trim().Length == longest).DistinctBy(item => item.Id).ToArray();

        DatabaseItem[] Exact(string candidate) => items.Where(item =>
            string.Equals(item.Name.Trim(), candidate, StringComparison.OrdinalIgnoreCase)).DistinctBy(item => item.Id).ToArray();
    }

    internal static string SearchText(string? text) => text is null ? "" : Refine.Replace(Quantity.Replace(text.Trim(), ""), "");

    internal static DatabaseItem[] Suggest(string text, IReadOnlyList<DatabaseItem> items)
    {
        var query = SearchText(text).ToUpperInvariant();
        if (query.Length < 5) return [];
        var candidates = items.Select(item => (Item: item, Name: item.Name.Trim().ToUpperInvariant()))
            .Where(entry => Math.Abs(entry.Name.Length - query.Length) <= 1 && OneEditApart(query, entry.Name))
            .Select(entry => entry.Item).DistinctBy(item => item.Id).OrderBy(item => item.Name).Take(8);
        return candidates.ToArray();
    }

    private static bool OneEditApart(string left, string right)
    {
        if (left.Length > right.Length) return OneEditApart(right, left);
        var different = left.Length != right.Length;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] == right[i]) continue;
            if (left.Length != right.Length) return left.AsSpan(i).SequenceEqual(right.AsSpan(i + 1));
            if (different) return false;
            different = true;
        }
        return different;
    }

    internal sealed record Screenshot(BitmapSource Image, double PointerX, double PointerY);

    internal static Screenshot? Capture(ForegroundProcess.WindowBounds bounds, Point cursor)
    {
        if (cursor.X < bounds.Left || cursor.X >= bounds.Right || cursor.Y < bounds.Top || cursor.Y >= bounds.Bottom) return null;
        int left = Math.Max(bounds.Left, (int)cursor.X - 220), top = Math.Max(bounds.Top, (int)cursor.Y - 47);
        int width = Math.Min(bounds.Right, (int)cursor.X + 300) - left, height = Math.Min(bounds.Bottom, (int)cursor.Y + 47) - top;
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        var dc = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = dc != IntPtr.Zero && bitmap != IntPtr.Zero ? SelectObject(dc, bitmap) : IntPtr.Zero;
        try
        {
            if (old == IntPtr.Zero || old == new IntPtr(-1) || !BitBlt(dc, 0, 0, width, height, screen, left, top, 0x00CC0020)) return null;
            var result = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            result.Freeze();
            return new Screenshot(result, cursor.X - left, cursor.Y - top);
        }
        finally
        {
            if (old != IntPtr.Zero && old != new IntPtr(-1)) SelectObject(dc, old);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    internal static async Task<string[]> ReadAsync(Screenshot capture)
    {
        var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US")) ??
                     OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return [];
        var gray = new FormatConvertedBitmap(capture.Image, PixelFormats.Gray8, null, 0);
        var pixels = new byte[gray.PixelWidth * gray.PixelHeight];
        gray.CopyPixels(pixels, gray.PixelWidth, 0);
        const int scale = 3;
        var width = gray.PixelWidth * scale;
        var height = gray.PixelHeight * scale;
        var enlarged = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                enlarged[y * width + x] = pixels[(y / scale) * gray.PixelWidth + x / scale];

        var lines = new List<(string Text, double Distance)>();
        await ReadPassAsync();
        for (var i = 0; i < enlarged.Length; i++) enlarged[i] = (byte)(255 - enlarged[i]);
        await ReadPassAsync();
        for (var i = 0; i < enlarged.Length; i++)
            enlarged[i] = (byte)Math.Clamp((127 - enlarged[i]) * 2 + 127, 0, 255);
        await ReadPassAsync();
        return lines.OrderBy(line => line.Distance).Select(line => line.Text).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        async Task ReadPassAsync()
        {
            using var writer = new DataWriter();
            writer.WriteBytes(enlarged);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Gray8, width, height);
            var result = await engine.RecognizeAsync(bitmap);
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0) continue;
                // The item tooltip normally sits immediately above the pointer.
                var left = line.Words.Min(word => word.BoundingRect.X) / scale;
                var right = line.Words.Max(word => word.BoundingRect.Right) / scale;
                var bottom = line.Words.Max(word => word.BoundingRect.Bottom) / scale;
                var horizontal = Math.Max(0, Math.Max(left - capture.PointerX - 12, capture.PointerX - right - 12));
                var vertical = capture.PointerY - bottom;
                if (horizontal > 65 || vertical < -25 || vertical > 43) continue;
                lines.Add((line.Text, Math.Abs(vertical - 9) + horizontal * .6 + (vertical < 0 ? 9 : 0)));
            }
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint flags);
}
