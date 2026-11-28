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

    private bool _renderingTimerTabs;

    private void RenderTimerTabs(int? editing = null)
    {
        _renderingTimerTabs = true;
        try
        {
            TimerTabStrip.Items.Clear();
            for (var tab = 0; tab < Math.Min(3, Settings.TimerTabs.Count); tab++)
                TimerTabStrip.Items.Add(CreateTimerTab(tab, editing == tab));
            if (Settings.TimerTabs.Count < 3)
            {
                var add = new TabItem { Header = "+" };
                add.SetResourceReference(ToolTipProperty, "TimerTabAdd");
                TimerTabStrip.Items.Add(add);
            }
            TimerTabStrip.SelectedIndex = Settings.ActiveTimerTab;
            if (editing is int index && TimerTabStrip.Items[index] is TabItem { Header: TextBox editor })
            {
                editor.Focus();
                editor.SelectAll();
            }
        }
        finally { _renderingTimerTabs = false; }
    }

    private TabItem CreateTimerTab(int index, bool editing)
    {
        var tab = new TabItem { Tag = index };
        if (!editing)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = Settings.TimerTabs[index], VerticalAlignment = VerticalAlignment.Center });
            if (index == Settings.ActiveTimerTab)
            {
                var remove = new Button { Content = "×", MinHeight = 0, MinWidth = 0, Padding = new Thickness(2, 0, 2, 0),
                    Margin = new Thickness(5, 0, 0, 0) };
                remove.SetResourceReference(StyleProperty, "GhostButtonStyle");
                remove.SetResourceReference(ToolTipProperty, "TimerTabDelete");
                remove.Click += (_, args) => { args.Handled = true; DeleteTimerTab(index); };
                header.Children.Add(remove);
            }
            tab.Header = header;
            tab.PreviewMouseLeftButtonDown += (_, args) =>
            {
                if (args.ClickCount != 2 || Settings.ActiveTimerTab != index || args.Source is Button) return;
                args.Handled = true;
                RenderTimerTabs(index);
            };
            return tab;
        }
        var name = new TextBox { Text = Settings.TimerTabs[index], Width = 90, MaxLength = 32 };
        tab.Header = name;
        var finished = false;
        void Finish(bool save)
        {
            if (finished) return;
            finished = true;
            if (save && !string.IsNullOrWhiteSpace(name.Text))
            {
                Settings.TimerTabs[index] = name.Text.Trim();
                RequestSave();
            }
            Dispatcher.BeginInvoke(new Action(() => { if (TimerTabStrip.Items.Contains(tab)) RenderTimerTabs(); }));
        }
        MouseButtonEventHandler outside = (_, _) => { if (!name.IsMouseOver) Finish(true); };
        PreviewMouseDown += outside;
        name.Unloaded += (_, _) => PreviewMouseDown -= outside;
        name.LostKeyboardFocus += (_, _) => Finish(true);
        name.KeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Enter or Key.Escape)) return;
            args.Handled = true;
            Finish(args.Key == Key.Enter);
        };
        return tab;
    }

    private void TimerTabStrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_renderingTimerTabs || e.Source != TimerTabStrip || TimerTabStrip.SelectedItem is not TabItem tab) return;
        if (tab.Tag is int index)
        {
            SelectTimerTab(index);
            return;
        }
        if (Settings.TimerTabs.Count >= 3) return;
        Settings.TimerTabs.Add((Settings.TimerTabs.Count + 1).ToString());
        Settings.ActiveTimerTab = Settings.TimerTabs.Count - 1;
        RequestSave();
        RenderTimerTabs(Settings.ActiveTimerTab);
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
