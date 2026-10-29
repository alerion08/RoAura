using System.Windows;
using RoAura.Models;

namespace RoAura;

public partial class MainWindow
{
    // Invoked only by the existing isolated package --smoke-test startup.
    internal void ValidateServerNavigationSmoke()
    {
        var selected = ServerProfileBox.SelectedIndex;
        var capture = Settings.PacketCaptureEnabled;
        try
        {
            Settings.PacketCaptureEnabled = false;
            ServerProfileBox.SelectedIndex = 3;
            if (Settings.ServerProfile != GameServerProfile.Toc || _monsters.Count != 61 || _items.Count != 9894)
                throw new InvalidOperationException("ToC profile did not load its catalogs.");
            if (!DatabaseNavButton.IsEnabled || !MvpNavButton.IsEnabled || LootNavButton.IsEnabled || GoldGoblinNavButton.IsEnabled || EffectsNavButton.IsEnabled)
                throw new InvalidOperationException("Packet-off navigation availability is incorrect.");
            foreach (var nav in new[] { EffectsNavButton, LootNavButton, GoldGoblinNavButton, DatabaseNavButton, MvpNavButton })
                if (nav.Visibility != Visibility.Visible) throw new InvalidOperationException("Unavailable navigation must remain visible.");
            if (PacketFeaturesToggle.IsChecked != false)
                throw new InvalidOperationException("Home packet toggle binding is incorrect.");
            ServerProfileBox.SelectedIndex = 2;
            if (DatabaseNavButton.IsEnabled || MvpNavButton.IsEnabled || DatabaseNavButton.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Unsupported-server navigation is incorrect.");
            ServerProfileBox.SelectedIndex = 3;
            Settings.PacketCaptureEnabled = true;
            if (!EffectsNavButton.IsEnabled || !LootNavButton.IsEnabled || !GoldGoblinNavButton.IsEnabled)
                throw new InvalidOperationException("Packet feature navigation did not restore.");
            Settings.PacketCaptureEnabled = false;
        }
        finally
        {
            ServerProfileBox.SelectedIndex = selected;
            Settings.PacketCaptureEnabled = capture;
        }
    }
}
