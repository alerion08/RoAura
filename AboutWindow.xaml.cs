using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using RoAura.Services;

namespace RoAura;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        // Version comes from RoAura.csproj <AssemblyVersion>; nothing else hardcodes it.
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        VersionRun.Text = " " + version;
        if (LocalizationService.CurrentLanguage == "en") CheckUpdatesButton.Content = "Check for updates";
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        try { await ((App)Application.Current).CheckForUpdatesAsync(true, this); }
        finally { CheckUpdatesButton.IsEnabled = true; }
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url }) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "RoAura", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
