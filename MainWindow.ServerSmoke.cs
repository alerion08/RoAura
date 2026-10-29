using System.Windows;
using RoAura.Models;

namespace RoAura;

public partial class MainWindow
{
    // Invoked only by the existing isolated package --smoke-test startup.
    internal void ValidateServerNavigationSmoke()
    {
        OpenSettings_Click(OpenSettingsButton, new RoutedEventArgs());
        if (MainTabs.SelectedIndex != 9 || !OpenSettingsButton.IsEnabled)
            throw new InvalidOperationException("Header settings button did not open Settings.");
        HomeNavButton.IsChecked = true;
        if (MainTabs.SelectedIndex != 0)
            throw new InvalidOperationException("Sidebar navigation did not restore after Settings.");
        var selected = ServerProfileBox.SelectedValue;
        var capture = Settings.PacketCaptureEnabled;
        try
        {
            Settings.PacketCaptureEnabled = false;
            ServerProfileBox.SelectedValue = GameServerProfile.Toc;
            if (Settings.ServerProfile != GameServerProfile.Toc || _monsters.Count != 61 || _items.Count != 9894)
                throw new InvalidOperationException("ToC profile did not load its catalogs.");
            if (!DatabaseNavButton.IsEnabled || !MvpNavButton.IsEnabled || LootNavButton.IsEnabled || GoldGoblinNavButton.IsEnabled || EffectsNavButton.IsEnabled)
                throw new InvalidOperationException("Packet-off navigation availability is incorrect.");
            foreach (var nav in new[] { EffectsNavButton, LootNavButton, GoldGoblinNavButton, DatabaseNavButton, MvpNavButton })
                if (nav.Visibility != Visibility.Visible) throw new InvalidOperationException("Unavailable navigation must remain visible.");
            if (PacketFeaturesToggle.IsChecked != false)
                throw new InvalidOperationException("Home packet toggle binding is incorrect.");
            ServerProfileBox.SelectedValue = GameServerProfile.Other;
            if (DatabaseNavButton.IsEnabled || MvpNavButton.IsEnabled || DatabaseNavButton.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Unsupported-server navigation is incorrect.");
            ServerProfileBox.SelectedValue = GameServerProfile.Toc;
            Settings.PacketCaptureEnabled = true;
            if (!EffectsNavButton.IsEnabled || !LootNavButton.IsEnabled || !GoldGoblinNavButton.IsEnabled)
                throw new InvalidOperationException("Packet feature navigation did not restore.");
            Settings.PacketCaptureEnabled = false;
        }
        finally
        {
            ServerProfileBox.SelectedValue = selected;
            Settings.PacketCaptureEnabled = capture;
        }
    }
}
