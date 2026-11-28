using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoAura.Services;

namespace RoAura;

public partial class MainWindow
{
    private void TimerTabStrip_Loaded(object sender, RoutedEventArgs e)
    {
        if (Settings.TimerTabs.Count == 0) Settings.TimerTabs.Add("MVP");
        RenderTimerTabs();
    }

    private void RenderTimerTabs(int? editing = null)
    {
        TimerTabStrip.Children.Clear();
        var count = Math.Min(3, Settings.TimerTabs.Count);
        for (var index = 0; index < count; index++)
        {
            var tab = index;
            if (editing == tab)
            {
                var name = new TextBox { Text = Settings.TimerTabs[tab], Width = 90, MaxLength = 32, Margin = new Thickness(2) };
                var finished = false;
                MouseButtonEventHandler? outside = null;
                void Finish(bool save)
                {
                    if (finished) return;
                    finished = true;
                    if (outside is not null) PreviewMouseDown -= outside;
                    if (save && !string.IsNullOrWhiteSpace(name.Text))
                    {
                        Settings.TimerTabs[tab] = name.Text.Trim();
                        RequestSave();
                    }
                    Dispatcher.BeginInvoke(new Action(() => { if (TimerTabStrip.Children.Contains(name)) RenderTimerTabs(); }));
                }
                name.KeyDown += (_, args) =>
                {
                    if (args.Key is not (Key.Enter or Key.Escape)) return;
                    args.Handled = true;
                    Finish(args.Key == Key.Enter);
                };
                outside = (_, args) =>
                {
                    if (!name.IsMouseOver) Finish(true);
                };
                PreviewMouseDown += outside;
                name.Unloaded += (_, _) => PreviewMouseDown -= outside;
                name.LostKeyboardFocus += (_, _) => Finish(true);
                TimerTabStrip.Children.Add(name);
                name.Focus();
                name.SelectAll();
                continue;
            }
            var button = new Button { Content = Settings.TimerTabs[tab], Margin = new Thickness(2), MaxWidth = 100 };
            if (tab == Settings.ActiveTimerTab) button.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
            button.Click += (_, _) => SelectTimerTab(tab);
            button.PreviewMouseLeftButtonDown += (_, args) =>
            {
                if (args.ClickCount != 2 || Settings.ActiveTimerTab != tab) return;
                args.Handled = true;
                RenderTimerTabs(tab);
            };
            var entry = new StackPanel { Orientation = Orientation.Horizontal };
            entry.Children.Add(button);
            if (tab == Settings.ActiveTimerTab)
            {
                var remove = new Button { Content = "×", Width = 24, Padding = new Thickness(0), Margin = new Thickness(0, 2, 4, 2) };
                remove.SetResourceReference(ToolTipProperty, "TimerTabDelete");
                remove.Click += (_, _) => DeleteTimerTab(tab);
                entry.Children.Add(remove);
            }
            TimerTabStrip.Children.Add(entry);
        }
        if (count >= 3) return;
        var add = new Button { Content = "+", Margin = new Thickness(2) };
        add.SetResourceReference(ToolTipProperty, "TimerTabAdd");
        add.Click += (_, _) =>
        {
            if (Settings.TimerTabs.Count >= 3) return;
            Settings.TimerTabs.Add(Settings.TimerTabs.Count == 1 ? "2" : "3");
            Settings.ActiveTimerTab = Settings.TimerTabs.Count - 1;
            RequestSave();
            RenderTimerTabs(Settings.ActiveTimerTab);
        };
        TimerTabStrip.Children.Add(add);
    }

    private void TimerQuickRestart_Click(object sender, RoutedEventArgs e) => RequestSave();

    private void TimerName_Commit(object sender, KeyboardFocusChangedEventArgs e) => RequestSave();

    private void TimerName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        RequestSave();
        e.Handled = true;
    }

    private void DeleteTimerTab(int index)
    {
        var name = Settings.TimerTabs[index];
        var title = T("Удалить вкладку", "Delete timer tab");
        if (!LootListNameDialog.Confirm(this,
                T($"Удалить вкладку «{name}» и все её таймеры?", $"Delete the “{name}” tab and all its timers?"), title))
            return;

        foreach (var timers in new[] { Settings.MvpTimers, Settings.TocMvpTimers })
        {
            foreach (var timer in timers.Where(timer => Settings.EffectiveTimerTab(timer) == index).ToArray())
                timers.Remove(timer);
            foreach (var timer in timers.Where(timer => timer.TabIndex > index))
                timer.TabIndex--;
            MvpTimerHotkeys.RefreshNumbers(timers);
        }
        Settings.TimerTabs.RemoveAt(index);
        if (Settings.TimerTabs.Count == 0) Settings.TimerTabs.Add("MVP");
        Settings.ActiveTimerTab = Math.Min(index, Settings.TimerTabs.Count - 1);
        Settings.ActiveTimerTabView.Refresh();
        RenderTimerTabs();
        RequestSave();
    }

    private void SelectTimerTab(int tab)
    {
        if (Settings.ActiveTimerTab == tab) return;
        Settings.ActiveTimerTab = tab;
        RequestSave();
    }
}
