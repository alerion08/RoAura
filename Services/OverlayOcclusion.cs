using System.Windows;
using System.Windows.Media;

namespace RoAura.Services;

/// <summary>Render-only exclusion of settings windows, in the overlay monitor's DIPs.</summary>
public static class OverlayOcclusion
{
    public static Rect ToLocalRect(Rect screenBounds, Point overlayScreenOrigin, Matrix fromDevice)
    {
        var topLeft = fromDevice.Transform(new Point(screenBounds.Left - overlayScreenOrigin.X,
            screenBounds.Top - overlayScreenOrigin.Y));
        var bottomRight = fromDevice.Transform(new Point(screenBounds.Right - overlayScreenOrigin.X,
            screenBounds.Bottom - overlayScreenOrigin.Y));
        return new Rect(topLeft, bottomRight);
    }

    public static Geometry? CreateClip(Size hostSize, IEnumerable<Rect> excludedWindows)
    {
        if (hostSize.IsEmpty || hostSize.Width <= 0 || hostSize.Height <= 0) return null;
        var host = new Rect(new Point(), hostSize);
        Geometry? result = null;
        foreach (var window in excludedWindows)
        {
            var overlap = Rect.Intersect(host, window);
            if (overlap.IsEmpty || overlap.Width <= 0 || overlap.Height <= 0) continue;
            result = new CombinedGeometry(GeometryCombineMode.Exclude,
                result ?? new RectangleGeometry(host), new RectangleGeometry(overlap));
        }
        result?.Freeze();
        return result;
    }
}
