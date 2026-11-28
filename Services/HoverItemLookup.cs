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

    internal static string SearchText(string? text)
    {
        if (text is null) return "";
        var cleaned = Refine.Replace(Quantity.Replace(text.Trim(), ""), "");
        cleaned = Regex.Replace(cleaned, @"[^\p{L}\p{N}'’+\-()\[\] ]+", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Count(char.IsLetter) >= 3 ? cleaned : "";
    }

    internal static DatabaseItem[] Suggest(string text, IReadOnlyList<DatabaseItem> items)
    {
        var query = Normalize(SearchText(text));
        if (query.Length < 5) return [];
        var limit = query.Length >= 11 ? 2 : 1;
        return items.Select(item => (Item: item, Name: Normalize(item.Name)))
            .Where(entry => Math.Abs(entry.Name.Length - query.Length) <= limit)
            .Select(entry => (entry.Item, Distance: EditDistance(query, entry.Name)))
            .Where(entry => entry.Distance <= limit)
            .OrderBy(entry => entry.Distance).ThenBy(entry => entry.Item.Name)
            .Select(entry => entry.Item).DistinctBy(item => item.Id).Take(8).ToArray();

        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var row = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            row[0] = i;
            for (var j = 1; j <= right.Length; j++)
                row[j] = Math.Min(Math.Min(previous[j] + 1, row[j - 1] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, row) = (row, previous);
        }
        return previous[^1];
    }

    internal static Int32Rect FindTooltip(byte[] pixels, int width, int height, double pointerX, double pointerY)
    {
        var edges = new List<(int Left, int Right, int Y)>();
        var best = Int32Rect.Empty;
        var bestScore = double.PositiveInfinity;
        var minY = Math.Max(0, (int)pointerY - 42);
        var maxY = Math.Min(height, (int)pointerY + 22);
        for (var y = minY; y < maxY; y++)
        for (var x = 0; x < width;)
        {
            if (pixels[y * width + x] >= 118) { x++; continue; }
            var start = x++;
            while (x < width && pixels[y * width + x] < 118) x++;
            if (x - start is < 35 or > 350 || start > pointerX + 45 || x < pointerX - 25) continue;
            foreach (var top in edges)
            {
                var h = y - top.Y;
                if (h is < 9 or > 27 || Math.Abs(top.Left - start) > 5 || Math.Abs(top.Right - x) > 5) continue;
                var left = Math.Max(top.Left, start);
                var right = Math.Min(top.Right, x);
                if (right - left < 35) continue;
                var light = 0;
                var dark = 0;
                for (var row = top.Y + 2; row < y - 1; row++)
                for (var col = left + 2; col < right - 2; col += 2)
                {
                    var value = pixels[row * width + col];
                    if (value >= 175) light++;
                    else if (value < 125) dark++;
                }
                var samples = (h - 3) * ((right - left - 3) / 2);
                if (light < 8 || dark < samples / 2 || light > samples / 2) continue;
                var horizontal = Math.Max(0, Math.Max(left - pointerX, pointerX - right));
                var score = Math.Abs(pointerY - y - 5) + horizontal * .7 + Math.Abs(h - 16) * .4;
                if (score >= bestScore) continue;
                bestScore = score;
                best = new Int32Rect(left, top.Y, right - left, h + 1);
            }
            edges.Add((start, x, y));
        }
        return best;
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
        var lines = new List<(string Text, double Distance)>();
        var tooltip = FindTooltip(pixels, gray.PixelWidth, gray.PixelHeight, capture.PointerX, capture.PointerY);
        if (!tooltip.IsEmpty)
        {
            await ReadPassAsync(tooltip, 4, value => (byte)(255 - value), -30);
            await ReadPassAsync(tooltip, 4, value => (byte)Math.Clamp((130 - value) * 3 + 127, 0, 255), -29);
        }
        var full = new Int32Rect(0, 0, gray.PixelWidth, gray.PixelHeight);
        await ReadPassAsync(full, 3, value => value, 0);
        await ReadPassAsync(full, 3, value => (byte)(255 - value), 0);
        return lines.OrderBy(line => line.Distance).Select(line => line.Text)
            .Where(text => SearchText(text).Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        async Task ReadPassAsync(Int32Rect area, int scale, Func<byte, byte> transform, double priority)
        {
            var width = area.Width * scale;
            var height = area.Height * scale;
            var enlarged = new byte[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                enlarged[y * width + x] = transform(pixels[(area.Y + y / scale) * gray.PixelWidth + area.X + x / scale]);
            using var writer = new DataWriter();
            writer.WriteBytes(enlarged);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Gray8, width, height);
            var result = await engine.RecognizeAsync(bitmap);
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0) continue;
                var left = area.X + line.Words.Min(word => word.BoundingRect.X) / scale;
                var right = area.X + line.Words.Max(word => word.BoundingRect.Right) / scale;
                var bottom = area.Y + line.Words.Max(word => word.BoundingRect.Bottom) / scale;
                var horizontal = Math.Max(0, Math.Max(left - capture.PointerX - 12, capture.PointerX - right - 12));
                var vertical = capture.PointerY - bottom;
                if (horizontal > 65 || vertical < -25 || vertical > 43) continue;
                lines.Add((line.Text, priority + Math.Abs(vertical - 9) + horizontal * .6 + (vertical < 0 ? 9 : 0)));
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
