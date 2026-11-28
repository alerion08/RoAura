using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RoAura.Services;

namespace RoAura;

public partial class OverlayWindow
{
    private Window? _overlayCursor;

    private void StartOverlayCursor()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => UpdateOverlayCursor();
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    private void UpdateOverlayCursor()
    {
        if (!IsVisible || !_overlaysVisible || !QuestWindowNative.IsSystemCursorHidden() ||
            !ForegroundProcess.TryGetForegroundAllowedBounds(_settings.AllowedProcesses, out var game))
        {
            _overlayCursor?.Hide();
            return;
        }

        var pointer = QuestWindowNative.Cursor();
        if (pointer.X < game.Left || pointer.X >= game.Right || pointer.Y < game.Top || pointer.Y >= game.Bottom ||
            !PointerCoversOverlay(pointer))
        {
            _overlayCursor?.Hide();
            return;
        }

        if (_overlayCursor is null)
        {
            _overlayCursor = new Window
            {
                Content = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M1,1 L1,16 L5,12 L8,19 L11,17 L8,10 L14,10 Z"),
                    Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1
                },
                Background = Brushes.Transparent, AllowsTransparency = true, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, IsHitTestVisible = false,
                Tag = PassiveOverlayCompanionTag
            };
            _overlayCursor.SourceInitialized += (_, _) => QuestWindowNative.Configure(_overlayCursor, true, true);
        }
        var dpi = QuestWindowNative.Scale(new WindowInteropHelper(_overlayCursor).EnsureHandle());
        QuestWindowNative.Place(_overlayCursor, new Rect(pointer, new Size(16 * dpi, 21 * dpi)));
        if (!_overlayCursor.IsVisible) _overlayCursor.Show();
    }

    private bool PointerCoversOverlay(Point pointer)
    {
        if (VisualTreeHelper.HitTest(this, PointFromScreen(pointer))?.VisualHit is FrameworkElement hit &&
            hit != this && hit != Content && hit is not System.Windows.Controls.Canvas) return true;
        if (_timerHeader?.IsVisible == true && _timerActions is { } actions &&
            QuestWindowNative.Bounds(new WindowInteropHelper(_timerHeader).Handle).Contains(pointer) &&
            VisualTreeHelper.HitTest(actions, actions.PointFromScreen(pointer)) is not null) return true;
        foreach (var popup in new[] { _mvpMapPopup })
            if (popup?.IsVisible == true && QuestWindowNative.Bounds(new WindowInteropHelper(popup).Handle).Contains(pointer))
                return true;
        return false;
    }
}
