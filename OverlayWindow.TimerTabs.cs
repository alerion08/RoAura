using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Services;

namespace RoAura;

public partial class OverlayWindow
{
    private Window? _timerHeader;
    private Button? _previousTimerTab, _nextTimerTab;
    private TextBlock? _timerTabTitle;

    private void UpdateTimerHeader()
    {
        if (!IsVisible || !_overlaysVisible || IsBusyEditing || !MvpTimerPanel.IsVisible ||
            TimerHeaderAnchor.ActualWidth <= 0 ||
            !ForegroundProcess.TryGetForegroundAllowedBounds(_settings.AllowedProcesses, out var game))
        {
            _timerHeader?.Hide();
            return;
        }

        if (_timerHeader is null)
        {
            var header = new Grid { Background = new SolidColorBrush(Color.FromArgb(232, 22, 39, 63)) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            _previousTimerTab = AddButton(0, "‹", "TimerTabPrevious", () => MoveTimerTab(-1));
            _timerTabTitle = new TextBlock
            {
                Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_timerTabTitle, 1);
            header.Children.Add(_timerTabTitle);
            _nextTimerTab = AddButton(2, "›", "TimerTabNext", () => MoveTimerTab(1));
            AddButton(3, "↻", "TimerTrackerRefresh", () =>
            {
                _settings.ActiveTimerTabView.Refresh();
                UpdateCooldowns();
                HideMvpMap();
            });
            _timerHeader = new Window
            {
                Content = header, AllowsTransparency = true, WindowStyle = WindowStyle.None, Background = Brushes.Transparent,
                ShowActivated = false, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize,
                Tag = "RoAura.GameInterface"
            };
            _timerHeader.SourceInitialized += (_, _) => QuestWindowNative.Configure(_timerHeader, false, true);

            Button AddButton(int column, string caption, string tooltip, Action run)
            {
                var button = new Button { Content = caption, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0,
                    Margin = new Thickness(2), Focusable = false };
                button.SetResourceReference(FrameworkElement.ToolTipProperty, tooltip);
                button.Click += (_, _) => run();
                Grid.SetColumn(button, column);
                header.Children.Add(button);
                return button;
            }
        }

        var selected = _settings.ActiveTimerTab;
        var count = Math.Min(3, _settings.TimerTabs.Count);
        _timerTabTitle!.Text = count == 0 ? "MVP" : _settings.TimerTabs[selected];
        _previousTimerTab!.Visibility = selected > 0 ? Visibility.Visible : Visibility.Hidden;
        _nextTimerTab!.Visibility = selected + 1 < count ? Visibility.Visible : Visibility.Hidden;
        var origin = TimerHeaderAnchor.PointToScreen(new Point());
        var end = TimerHeaderAnchor.PointToScreen(new Point(TimerHeaderAnchor.ActualWidth, TimerHeaderAnchor.ActualHeight));
        if (origin.X < game.Left || origin.Y < game.Top || end.X > game.Right || end.Y > game.Bottom)
        {
            _timerHeader.Hide();
            return;
        }
        QuestWindowNative.Place(_timerHeader, new Rect(origin, end));
        if (!_timerHeader.IsVisible) _timerHeader.Show();
    }

    private void MoveTimerTab(int offset)
    {
        _settings.ActiveTimerTab += offset;
        _store.Save(_settings);
        HideMvpMap();
        UpdateTimerHeader();
    }
}
