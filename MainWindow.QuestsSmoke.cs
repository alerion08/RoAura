using System.Windows;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MainWindow
{
    // Only the existing isolated --smoke-test path calls this; no live data/capture.
    internal async Task ValidateQuestsSmokeAsync()
    {
        var server = Settings.ServerProfile; var packets = Settings.PacketCaptureEnabled; var language = Settings.Language;
        try
        {
            Settings.PacketCaptureEnabled = false;
            foreach (var profile in new[] { "RTM", "SpringRO", "ToC", "Other" })
            {
                ServerProfileBox.SelectedValue = profile;
                QuestsNavButton.IsChecked = true;
                if (!QuestsNavButton.IsEnabled || MainTabs.SelectedIndex != 10)
                    throw new InvalidOperationException("Offline quest navigation failed for " + profile);
                OpenSettings_Click(OpenSettingsButton, new RoutedEventArgs());
                if (MainTabs.SelectedIndex != 9) throw new InvalidOperationException("Quests displaced Settings.");
            }
            ServerProfileBox.SelectedValue = "SpringRO";
            QuestsNavButton.IsChecked = true;
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Data", "questie_catalog.sqlite")))
            {
                await _questWorkspace.LoadCatalogAsync();
                if (_questWorkspace.Catalog?.Guides.Count != 286) throw new InvalidOperationException("Quest catalog did not load in the real app.");
                await _questWorkspace.SelectProfileAsync(new("SpringRO", null, QuestWorkspace.LocalProfile), default);
                _questWorkspace.SelectedGuide = _questWorkspace.Catalog.Guides["irowiki-classic:Lost_Child_Quest"];
                _questWorkspace.AddSelected(); _questWorkspace.SelectStep(3);
                _questWorkspace.SelectedGuide = _questWorkspace.Catalog.Guides["irowiki-classic:Friendship_Quest"];
                _questWorkspace.AddSelected();
                var lost = _questWorkspace.Active.Single(q => q.Progress.QuestKey.EndsWith(":Lost_Child_Quest"));
                _questWorkspace.Move(lost, 1); _questWorkspace.Move(lost, -1);
                if (lost.Progress.CurrentStep != 3 || _questWorkspace.Active.Single(q => q.Progress.QuestKey.EndsWith(":Friendship_Quest")).Progress.CurrentStep != 1)
                    throw new InvalidOperationException("Quest UI controller did not retain independent instructions.");
                if (!await _questWorkspace.FlushAsync()) throw new InvalidOperationException(_questWorkspace.Error);
                await QuestPage.ValidateSmokeAsync(_questWorkspace);
            }
        }
        finally
        {
            Settings.Language = language; LocalizationService.ApplyLanguage(language); _questWorkspace.RefreshLanguage();
            ServerProfileBox.SelectedValue = server; Settings.PacketCaptureEnabled = packets;
            HomeNavButton.IsChecked = true;
        }
    }
}
