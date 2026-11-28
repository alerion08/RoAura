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
    internal bool IsMvpMapVisible => _mvpMapPopup?.IsVisible == true;
    private MvpTimer? _hoveredMvp;
    private double _mvpPopupHeight;
    private Rect _mvpPopupPlacement;
    private DateTime _mvpHoverSince;

    private static IEnumerable<FrameworkElement> TimerLabels(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement label && label.Tag as string == "MvpTimerHover" && label.DataContext is MvpTimer) yield return label;
            foreach (var nested in TimerLabels(child)) yield return nested;
        }
    }
    private void RefreshMvpMapHover()
    {
        if (!IsVisible || !_overlaysVisible || _timerPanelEditMode || IsLayoutEditing || MvpTimerPanel.Visibility != Visibility.Visible ||
            !ForegroundProcess.TryGetForegroundAllowedBounds(_settings.AllowedProcesses, out var game))
        { HideMvpMap(); return; }
        var cursor = QuestWindowNative.Cursor();
        if (IsOverTimerRestart(cursor)) { HideMvpMap(); return; }
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
            var content = new StackPanel();
            var header = CreateMvpMapHeader(selected);
            if (header is not null) content.Children.Add(header);
            content.Children.Add(map);
            _mvpPopupHeight = header is null ? 305 : 353;
            _mvpMapPopup = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Tag = PassiveOverlayCompanionTag, Background = Brushes.Transparent, ShowActivated = false, ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize, Width = 280, Height = _mvpPopupHeight,
                Content = new Border { Child = content, CornerRadius = new CornerRadius(6),
                    Background = (Brush)FindResource("RoWindowBrush") } };
            AppearanceService.ApplyPalette(_mvpMapPopup,
                AppearanceService.Resolve(_settings.ApplicationTheme, _settings.GameOverlayTheme, true));
            ((Border)_mvpMapPopup.Content).SetResourceReference(Border.BackgroundProperty, "RoWindowBrush");
            _mvpMapPopup.SourceInitialized += (_, _) => QuestWindowNative.Configure(_mvpMapPopup, true, true);

        }
        var dpi = QuestWindowNative.Scale(new System.Windows.Interop.WindowInteropHelper(_mvpMapPopup).EnsureHandle());
        var width = 280*dpi; var height = _mvpPopupHeight*dpi;
        var panel = new Rect(MvpTimerPanel.PointToScreen(new Point()),
            MvpTimerPanel.PointToScreen(new Point(MvpTimerPanel.ActualWidth, MvpTimerPanel.ActualHeight)));
        var placement = MvpMapPlacement.Find(panel, new Size(width,height),
            new Rect(game.Left,game.Top,game.Right-game.Left,game.Bottom-game.Top),8*dpi);
        if (placement.IsEmpty || _controlScreenBounds.Any(rect => rect.IntersectsWith(placement))) { HideMvpMap(); return; }
        if (!_mvpMapPopup.IsVisible || _mvpPopupPlacement != placement ||
            !QuestWindowNative.IsAbove(new System.Windows.Interop.WindowInteropHelper(_mvpMapPopup).Handle,
                new System.Windows.Interop.WindowInteropHelper(this).Handle))
        {
            QuestWindowNative.Place(_mvpMapPopup, placement);
            _mvpPopupPlacement = placement;
        }
        if (!_mvpMapPopup.IsVisible) _mvpMapPopup.Show();
    }
    private FrameworkElement? CreateMvpMapHeader(MvpTimer timer)
    {
        if (!_settings.TimersPopupIcon && !_settings.TimersPopupName && !_settings.TimersPopupMap)
            return null;
        var header = new DockPanel { Height = 48, Margin = new Thickness(12, 8, 12, 0) };
        if (_settings.TimersPopupIcon && new RoAura.Converters.TimerImageConverter().Convert(
                timer.TimerImage, typeof(ImageSource), null!, System.Globalization.CultureInfo.CurrentCulture) is ImageSource source)
        {
            var icon = new Image { Source = source, Width = 32, Height = 32,
                Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            header.Children.Add(icon);
        }
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (_settings.TimersPopupName) AddLabel(timer.Name, true);
        if (_settings.TimersPopupMap) AddLabel(timer.LocationLabel, false);
        header.Children.Add(labels);
        return header;

        void AddLabel(string text, bool emphasized)
        {
            var label = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = emphasized ? FontWeights.SemiBold : FontWeights.Normal, ToolTip = text };
            label.SetResourceReference(TextBlock.ForegroundProperty, "RoTextBrush");
            labels.Children.Add(label);
        }
    }

    internal void RefreshTimerMapAppearance() => HideMvpMap();

    private void HideMvpMap()
    {
        _mvpMapPopup?.Close(); _mvpMapPopup = null; _hoveredMvp = null;
    }
}
