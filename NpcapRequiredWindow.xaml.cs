using System.Diagnostics;
using System.Windows;
using RoAura.Services;

namespace RoAura;

public partial class NpcapRequiredWindow : Window
{
    private const string NpcapUrl = "https://npcap.com/#download";

    public NpcapRequiredWindow() => InitializeComponent();

    private void Download_Click(object sender, RoutedEventArgs e) => OpenUrl(NpcapUrl);

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (NpcapPacketCapture.IsNpcapInstalled())
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this,
            LocalizationService.Get("NpcapNotFound"),
            "RoAura", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{LocalizationService.Get("OpenLinkFailed")}: {ex.Message}", "RoAura",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
