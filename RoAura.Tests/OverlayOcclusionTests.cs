using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class OverlayOcclusionTests
{
    [Fact]
    public void SettingsOnAnotherMonitorLeaveTheWholeGameVisible()
    {
        RunSta(() =>
        {
            var settings = OverlayOcclusion.ToLocalRect(new Rect(-1920, 100, 1400, 900),
                new Point(0, 0), Matrix.Identity);
            Assert.Null(OverlayOcclusion.CreateClip(new Size(2560, 1440), [settings]));
        });
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    public void SameMonitorExcludesOnlyTheSettingsWindowAndOwnedDialog(double dpiScale)
    {
        RunSta(() =>
        {
            var fromDevice = new Matrix(1 / dpiScale, 0, 0, 1 / dpiScale, 0, 0);
            var origin = new Point(-2560, -200);
            Rect Local(double x, double y, double width, double height) => OverlayOcclusion.ToLocalRect(
                new Rect(origin.X + x * dpiScale, origin.Y + y * dpiScale, width * dpiScale, height * dpiScale), origin, fromDevice);
            var main = Local(100, 100, 700, 500);
            var dialog = Local(750, 200, 300, 300);
            var clip = OverlayOcclusion.CreateClip(new Size(1600, 900), [main, dialog]);
            Assert.NotNull(clip);
            Assert.True(clip.IsFrozen);
            Assert.False(clip.FillContains(new Point(200, 200)));
            Assert.False(clip.FillContains(new Point(1000, 300)));
            Assert.True(clip.FillContains(new Point(50, 50)));
            Assert.True(clip.FillContains(new Point(1400, 700)));
            // Changing focus/closing the settings restores the full game surface.
            Assert.Null(OverlayOcclusion.CreateClip(new Size(1600, 900), []));
        });
    }

    [Fact]
    public void MovingPartiallyOffscreenAndMaximizingSettingsHandleWindowIntersections()
    {
        RunSta(() =>
        {
            var clip = OverlayOcclusion.CreateClip(new Size(1000, 700), [new Rect(-500, 0, 600, 400)]);
            Assert.NotNull(clip);
            Assert.False(clip.FillContains(new Point(50, 100)));
            Assert.True(clip.FillContains(new Point(200, 100)));
            var covered = OverlayOcclusion.CreateClip(new Size(1000, 700), [new Rect(-10, -10, 1100, 800)]);
            Assert.NotNull(covered);
            Assert.False(covered.FillContains(new Point(500, 350)));
            Assert.Null(OverlayOcclusion.CreateClip(new Size(0, 0), [new Rect(1, 1, 200, 200)]));
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Occlusion test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
