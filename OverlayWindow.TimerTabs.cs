using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class OverlayWindow
{
    internal event Action<MvpTimer>? TimerRestartRequested;
    private Border? _restartHint;
    internal bool IsTimerRestartHintVisible => _restartHint?.IsVisible == true;
    internal Button? FindTimerRestartButton(MvpTimer timer) => _timerRefreshButtons.GetValueOrDefault(timer);

    private const double TimerActionWidth = 19;
    private Window? _timerHeader;
    private Button? _previousTimerTab, _nextTimerTab;
    private TextBlock? _timerTabTitle;
    private Grid? _timerTabHeading;
    private Canvas? _timerActions;
    private readonly Dictionary<MvpTimer, Button> _timerRefreshButtons = [];
    private Rect _timerActionBounds = Rect.Empty;
    private Rect _timerActionGameBounds = Rect.Empty;
    private Rect[] _timerOcclusion = [];
    private Rect _timerPanelBounds = Rect.Empty;

    private void UpdateTimerHeader()
    {
        if (!IsVisible || !_overlaysVisible || !MvpTimerPanel.IsVisible)
        {
            HideRestartHint();
            _timerHeader?.Hide();
            return;
        }

        UpdateTimerTrackerSize();
        if (IsBusyEditing || TimerHeaderAnchor.ActualWidth <= 0 || _gameBounds is not { } game ||
            !ForegroundProcess.TryGetForegroundAllowedBounds(_settings.AllowedProcesses, out _) &&
            !Application.Current.Windows.OfType<Window>().Any(window => window.IsVisible &&
                (window is DatabaseOverlayWindow or DatabaseDetailWindow or QuestBrowserWindow) &&
                ForegroundProcess.IsForegroundWindow(new WindowInteropHelper(window).Handle)))
        {
            HideRestartHint();
            _timerHeader?.Hide();
            return;
        }

        if (_timerHeader is null) CreateTimerActions();
        var selected = _settings.ActiveTimerTab;
        var count = Math.Min(3, _settings.TimerTabs.Count);
        _timerTabTitle!.Text = count == 0 ? "MVP" : _settings.TimerTabs[selected];
        _previousTimerTab!.Visibility = selected > 0 ? Visibility.Visible : Visibility.Hidden;
        _nextTimerTab!.Visibility = selected + 1 < count ? Visibility.Visible : Visibility.Hidden;

        var occlusion = _controlScreenBounds.Concat(Application.Current.Windows.OfType<Window>()
            .Where(window => window.IsVisible && window is DatabaseOverlayWindow or DatabaseDetailWindow or QuestBrowserWindow)
            .Select(window => QuestWindowNative.Bounds(new WindowInteropHelper(window).Handle)))
            .Where(rect => !rect.IsEmpty).Distinct().ToArray();
        var panelOrigin = MvpTimerPanel.PointToScreen(new Point());
        var panelEnd = MvpTimerPanel.PointToScreen(new Point(MvpTimerPanel.ActualWidth, MvpTimerPanel.ActualHeight));
        var panelBounds = new Rect(panelOrigin, panelEnd);
        var occlusionChanged = !_timerOcclusion.SequenceEqual(occlusion);
        if (occlusionChanged || _timerPanelBounds != panelBounds)
        {
            _timerPanelBounds = panelBounds;
            _timerOcclusion = occlusion;
            MvpTimerPanel.Clip = QuestWindowNative.ExclusionClip(MvpTimerPanel,
                new Size(MvpTimerPanel.ActualWidth, MvpTimerPanel.ActualHeight), occlusion);
        }

        var origin = TimerHeaderAnchor.PointToScreen(new Point());
        var end = MvpTimerPanel.PointToScreen(new Point(MvpTimerPanel.ActualWidth, MvpTimerPanel.ActualHeight));
        end.X = TimerHeaderAnchor.PointToScreen(new Point(TimerHeaderAnchor.ActualWidth, 0)).X;
        var gameBounds = new Rect(game.Left, game.Top, game.Right - game.Left, game.Bottom - game.Top);
        if (end.X <= origin.X || end.Y <= origin.Y || !gameBounds.IntersectsWith(new Rect(origin, end)))
        {
            HideRestartHint();
            _timerHeader!.Hide();
            return;
        }

        _timerTabHeading!.Width = TimerHeaderAnchor.ActualWidth;
        _timerTabHeading.Height = TimerHeaderAnchor.ActualHeight;
        UpdateTimerRefreshButtons(origin);
        if (_restartHint is not null && !IsOverTimerRestart(QuestWindowNative.Cursor())) HideRestartHint();
        var bounds = new Rect(origin, end);
        if (occlusion.Any(rect => rect.Contains(bounds)))
        {
            HideRestartHint();
            _timerHeader!.Hide();
            return;
        }
        var needsClip = occlusionChanged || _timerActionBounds != bounds || _timerActionGameBounds != gameBounds || !_timerHeader!.IsVisible;
        if (needsClip)
        {
            QuestWindowNative.Place(_timerHeader!, bounds);
            _timerActionBounds = bounds;
            _timerActionGameBounds = gameBounds;
            if (!_timerHeader!.IsVisible) _timerHeader.Show();
            _timerActions!.UpdateLayout();
            _timerActions.Clip = QuestWindowNative.ExclusionClip(_timerActions,
                new Size(_timerHeader.Width, _timerHeader.Height), WithGameEdges(occlusion, bounds, gameBounds));
        }
    }

    private static IEnumerable<Rect> WithGameEdges(IEnumerable<Rect> excluded, Rect bounds, Rect game)
    {
        foreach (var rect in excluded) yield return rect;
        if (bounds.Left < game.Left) yield return new Rect(bounds.Left, bounds.Top, game.Left - bounds.Left, bounds.Height);
        if (bounds.Right > game.Right) yield return new Rect(game.Right, bounds.Top, bounds.Right - game.Right, bounds.Height);
        if (bounds.Top < game.Top) yield return new Rect(bounds.Left, bounds.Top, bounds.Width, game.Top - bounds.Top);
        if (bounds.Bottom > game.Bottom) yield return new Rect(bounds.Left, game.Bottom, bounds.Width, bounds.Bottom - game.Bottom);
    }

    private void UpdateTimerTrackerSize()
    {
        var sizes = TimerLabels(TimerRowsMeasure)
            .Where(row => row.DataContext is MvpTimer { IsVisible: true })
            .Select(row => (Tab: _settings.EffectiveTimerTab((MvpTimer)row.DataContext),
                Width: row.DesiredSize.Width, Height: row.DesiredSize.Height)).ToArray();
        var padding = MvpTimerPanel.Padding;
        var border = MvpTimerPanel.BorderThickness;
        var width = sizes.Select(row => row.Width).DefaultIfEmpty(0).Max();
        var height = sizes.GroupBy(row => row.Tab).Select(tab => tab.Sum(row => row.Height)).DefaultIfEmpty(0).Max();
        MvpTimerPanel.MinWidth = Math.Max(TimerHeaderAnchor.MinWidth, Math.Ceiling(width) + TimerActionWidth + 5) +
            padding.Left + padding.Right + border.Left + border.Right;
        MvpTimerPanel.MinHeight = TimerHeaderAnchor.Height + Math.Ceiling(height) +
            padding.Top + padding.Bottom + border.Top + border.Bottom;
    }

    private void CreateTimerActions()
    {
        _timerActions = new Canvas();
        var header = _timerTabHeading = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        _previousTimerTab = TimerAction("‹", "TimerTabPrevious", () => MoveTimerTab(-1));
        _nextTimerTab = TimerAction("›", "TimerTabNext", () => MoveTimerTab(1));
        _timerTabTitle = new TextBlock
        {
            Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14,
            Effect = TimerTextShadow(), TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        Grid.SetColumn(_timerTabTitle, 1);
        Grid.SetColumn(_nextTimerTab, 2);
        header.Children.Add(_previousTimerTab);
        header.Children.Add(_timerTabTitle);
        header.Children.Add(_nextTimerTab);
        _timerActions.Children.Add(header);
        _timerHeader = new Window
        {
            Content = _timerActions, AllowsTransparency = true, WindowStyle = WindowStyle.None, Background = Brushes.Transparent,
            ShowActivated = false, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize,
            Tag = PassiveOverlayCompanionTag
        };
        _timerHeader.SourceInitialized += (_, _) => QuestWindowNative.Configure(_timerHeader, false, true);
    }

    private void UpdateTimerRefreshButtons(Point origin)
    {
        var visible = new HashSet<MvpTimer>();
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        foreach (var row in TimerLabels(TimerRows).Where(row => row.IsVisible && row.ActualHeight > 0))
        {
            if (row.DataContext is not MvpTimer timer || !visible.Add(timer)) continue;
            if (!_timerRefreshButtons.TryGetValue(timer, out var button))
            {
                button = TimerAction("↻", "TimerTrackerRefreshOne", () =>
                {
                    HideRestartHint();
                    HideMvpMap();
                    TimerRestartRequested?.Invoke(timer);
                });
                button.Width = TimerActionWidth;
                button.Height = 17;
                button.FontSize = 13;
                button.ClearValue(FrameworkElement.ToolTipProperty);
                var restartButton = button;
                button.MouseEnter += (_, _) => ShowRestartHint(restartButton);
                button.MouseLeave += (_, _) => HideRestartHint();
                _timerRefreshButtons.Add(timer, button);
                _timerActions!.Children.Add(button);
            }
            Canvas.SetLeft(button, TimerHeaderAnchor.ActualWidth - TimerActionWidth);
            Canvas.SetTop(button, (row.PointToScreen(new Point()).Y - origin.Y) / scale + (row.ActualHeight - button.Height) / 2);
        }
        foreach (var timer in _timerRefreshButtons.Keys.Where(timer => !visible.Contains(timer)).ToArray())
        {
            if (_timerRefreshButtons[timer].IsMouseOver) HideRestartHint();
            _timerActions!.Children.Remove(_timerRefreshButtons[timer]);
            _timerRefreshButtons.Remove(timer);
        }
    }

    private Button TimerAction(string caption, string tooltip, Action run)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = caption, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Effect = TimerTextShadow() },
            Foreground = Brushes.White,
            Style = (Style)FindResource("TimerOverlayActionStyle")
        };
        button.SetResourceReference(FrameworkElement.ToolTipProperty, tooltip);
        button.Click += (_, _) => run();
        return button;
    }

    private static DropShadowEffect TimerTextShadow() =>
        new() { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 4, Opacity = 1 };

    private bool IsOverTimerRestart(Point pointer)
    {
        if (_timerHeader?.IsVisible != true || _timerOcclusion.Any(rect => rect.Contains(pointer))) return false;
        return _timerRefreshButtons.Values.Any(button => button.IsVisible &&
            new Rect(button.PointToScreen(new Point()), button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight)))
                .Contains(pointer));
    }

    private void ShowRestartHint(Button button)
    {
        if (_timerActions is not { } actions || !IsOverTimerRestart(QuestWindowNative.Cursor())) return;
        HideMvpMap();
        HideRestartHint();
        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(246, 21, 33, 47)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(191, 208, 230)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3, 6, 3),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = LocalizationService.Get("TimerTrackerRefreshOne"), Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap
            }
        };
        hint.Measure(new Size(TimerHeaderAnchor.ActualWidth, double.PositiveInfinity));
        Canvas.SetLeft(hint, Math.Max(0, Canvas.GetLeft(button) + button.Width - hint.DesiredSize.Width));
        Canvas.SetTop(hint, Math.Max(0, Canvas.GetTop(button) - hint.DesiredSize.Height - 3));
        Panel.SetZIndex(hint, 1);
        actions.Children.Add(hint);
        _restartHint = hint;
    }

    private void HideRestartHint()
    {
        if (_restartHint is null) return;
        _timerActions?.Children.Remove(_restartHint);
        _restartHint = null;
    }

    private void MoveTimerTab(int offset)
    {
        _settings.ActiveTimerTab += offset;
        _store.Save(_settings);
        HideMvpMap();
        UpdateTimerHeader();
    }
}
