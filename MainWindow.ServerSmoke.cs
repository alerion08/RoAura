using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RoAura.Models;
using RoAura.Services;

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
        var processes = Settings.AllowedProcesses.ToList();
        try
        {
            Settings.PacketCaptureEnabled = false;
            ServerProfileBox.SelectedValue = GameServerProfile.Toc;
            if (Settings.ServerProfile != GameServerProfile.Toc || _monsters.Count != 1061 ||
                _monsters.Count(monster => monster.IsMvp) != 61 || _items.Count != 9894)
                throw new InvalidOperationException("ToC profile did not load its catalogs.");
            if (!DatabaseNavButton.IsEnabled || !MvpNavButton.IsEnabled || LootNavButton.IsEnabled || GoldGoblinNavButton.IsEnabled || EffectsNavButton.IsEnabled)
                throw new InvalidOperationException("Packet-off navigation availability is incorrect.");
            foreach (var nav in new[] { EffectsNavButton, LootNavButton, GoldGoblinNavButton, DatabaseNavButton, MvpNavButton })
                if (nav.Visibility != Visibility.Visible) throw new InvalidOperationException("Unavailable navigation must remain visible.");
            if (PacketFeaturesToggle.IsChecked != false)
                throw new InvalidOperationException("Home packet toggle binding is incorrect.");
            ServerProfileBox.SelectedValue = GameServerProfile.Other;
            if (DatabaseNavButton.IsEnabled || !MvpNavButton.IsEnabled || LootNavButton.IsEnabled || DatabaseNavButton.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Unsupported-server navigation is incorrect.");
            ServerProfileBox.SelectedValue = GameServerProfile.MuhRo;
            Settings.PacketCaptureEnabled = true;
            if (Settings.ServerProfile != GameServerProfile.MuhRo || !EffectsNavButton.IsEnabled ||
                !CooldownNavButton.IsEnabled || !EquipmentNavButton.IsEnabled || !LootNavButton.IsEnabled ||
                !MvpNavButton.IsEnabled || DatabaseNavButton.IsEnabled ||
                !Settings.AllowedProcesses.Contains("Muh", StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("MuhRO packet profile or client discovery is incorrect.");
            if (((FrameworkElement)_overlay.FindName("RateIndicatorsPanel")).Visibility != Visibility.Visible ||
                ((FrameworkElement)_overlay.FindName("RewardGroupsPanel")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("MuhRO statistics overlays are hidden.");
            Settings.PacketCaptureEnabled = false;
            if (LootNavButton.IsEnabled || !MvpNavButton.IsEnabled)
                throw new InvalidOperationException("MuhRO packet gate must disable statistics but keep manual timers.");
            ServerProfileBox.SelectedValue = GameServerProfile.Toc;
            Settings.PacketCaptureEnabled = true;
            if (!EffectsNavButton.IsEnabled || !LootNavButton.IsEnabled || !GoldGoblinNavButton.IsEnabled)
                throw new InvalidOperationException("Packet feature navigation did not restore.");
            ValidateEffectTutorialSmoke();
            Settings.PacketCaptureEnabled = false;
        }
        finally
        {
            ServerProfileBox.SelectedValue = selected;
            Settings.PacketCaptureEnabled = capture;
            Settings.AllowedProcesses = processes;
        }
    }

    private void ValidateEffectTutorialSmoke()
    {
        EffectsNavButton.IsChecked = true;
        UpdateLayout();
        if (!EffectTutorialButton.IsVisible || !File.Exists(EffectTutorialWindow.VideoPath))
            throw new InvalidOperationException("Effects tutorial button or packaged video is missing.");

        var timeout = Stopwatch.StartNew();
        var probe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
        var playbackStarted = false;
        probe.Tick += (_, _) =>
        {
            var window = OwnedWindows.OfType<EffectTutorialWindow>().SingleOrDefault();
            if (window is null) return;
            playbackStarted = window.TutorialVideo.Position > TimeSpan.Zero;
            if (playbackStarted || window.PlaybackFailed || timeout.Elapsed >= TimeSpan.FromSeconds(5))
                window.Close();
        };
        try
        {
            probe.Start();
            EffectTutorialButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!playbackStarted)
                throw new InvalidOperationException("The packaged effect tutorial did not start playing.");
            DebugLog.Write("EFFECT TUTORIAL SMOKE PASS: Effects button opens the popup and the local MP4 plays.");
        }
        finally
        {
            probe.Stop();
            HomeNavButton.IsChecked = true;
        }
    }
}
