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
                void Finish(bool save)
                {
                    if (finished) return;
                    finished = true;
                    if (save && !string.IsNullOrWhiteSpace(name.Text))
                    {
                        Settings.TimerTabs[tab] = name.Text.Trim();
                        RequestSave();
                    }
                    RenderTimerTabs();
                }
                name.KeyDown += (_, args) =>
                {
                    if (args.Key is not (Key.Enter or Key.Escape)) return;
                    args.Handled = true;
                    Finish(args.Key == Key.Enter);
                };
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
            TimerTabStrip.Children.Add(button);
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

    private void SelectTimerTab(int tab)
    {
        if (Settings.ActiveTimerTab == tab) return;
        Settings.ActiveTimerTab = tab;
        RequestSave();
    }
}
