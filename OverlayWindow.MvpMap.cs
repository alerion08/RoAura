using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class OverlayWindow
{
    private Window? _mvpMapPopup;
    private MvpTimer? _hoveredMvp;
    private DateTime _mvpHoverSince;

    private static IEnumerable<TextBlock> TimerLabels(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock label && label.DataContext is MvpTimer) yield return label;
            foreach (var nested in TimerLabels(child)) yield return nested;
        }
    }
    private void RefreshMvpMapHover()
    {
        if (!IsVisible || !_overlaysVisible || _timerPanelEditMode || MvpTimerPanel.Visibility != Visibility.Visible ||
            !ForegroundProcess.TryGetForegroundAllowedBounds(_settings.AllowedProcesses, out var game))
        { HideMvpMap(); return; }
        var cursor = QuestWindowNative.Cursor();
        MvpTimer? selected = null;
        Rect hit = Rect.Empty;
        foreach (var label in TimerLabels(MvpTimerPanel).Where(label => label.IsVisible && label.ActualWidth > 0))
        {
            var bounds = new Rect(label.PointToScreen(new Point()), label.PointToScreen(new Point(label.ActualWidth, label.ActualHeight)));
            if (!bounds.Contains(cursor)) continue;
            selected = label.DataContext as MvpTimer; hit = bounds; break;
        }
        if (selected is null || string.IsNullOrEmpty(selected.LocationCode)) { HideMvpMap(); return; }
        if (!ReferenceEquals(selected, _hoveredMvp))
        { HideMvpMap(); _hoveredMvp = selected; _mvpHoverSince = DateTime.UtcNow; return; }
        if (DateTime.UtcNow - _mvpHoverSince < TimeSpan.FromMilliseconds(400)) return;
        if (_mvpMapPopup is null)
        {
            var map = new MvpMapEditor { ReadOnly = true, DataContext = selected, Margin = new Thickness(10) };
            _mvpMapPopup = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Tag = "RoAura.GameInterface", Background = Brushes.Transparent, ShowActivated = false, ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize, Width = 280, Height = 305,
                Content = new Border { Child = map, CornerRadius = new CornerRadius(6),
                    Background = (Brush)FindResource("RoWindowBrush") } };
            AppearanceService.ApplyPalette(_mvpMapPopup,
                AppearanceService.Resolve(_settings.ApplicationTheme, _settings.GameOverlayTheme, true));
            ((Border)_mvpMapPopup.Content).SetResourceReference(Border.BackgroundProperty, "RoWindowBrush");
            _mvpMapPopup.SourceInitialized += (_, _) => QuestWindowNative.Configure(_mvpMapPopup, true, true);
            _mvpMapPopup.Show();
        }
        var dpi = QuestWindowNative.Scale(new System.Windows.Interop.WindowInteropHelper(_mvpMapPopup).Handle);
        var width = 280*dpi; var height = 305*dpi;
        var left = Math.Max(game.Left, Math.Min(hit.Left, game.Right-width));
        var top = hit.Bottom + 8;
        if (top+height > game.Bottom) top = Math.Max(game.Top, hit.Top-height-8);
        QuestWindowNative.Place(_mvpMapPopup, new Rect(left,top,width,height));
    }
    private void HideMvpMap()
    {
        _mvpMapPopup?.Close(); _mvpMapPopup = null; _hoveredMvp = null;
    }
}
