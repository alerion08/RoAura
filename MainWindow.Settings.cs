using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MainWindow
{
    internal void SelectSettingsServer(string profile) => ServerProfileBox.SelectedValue = GameServerProfile.Normalize(profile);
    internal void SelectSettingsLanguage(string language) => LanguageBox.SelectedValue = language;
    internal void SettingsConnectionAction(string action)
    {
        switch (action)
        {
            case "detect": AutoDetectConnection_Click(this, new RoutedEventArgs()); break;
            case "npcap": CheckNpcap_Click(this, new RoutedEventArgs()); break;
            case "aid": ResetPacketAid_Click(this, new RoutedEventArgs()); break;
        }
    }
    internal void SaveSharedSettings()
    {
        if (_applyingPreset || Settings is null) return;
        UpdateNavigationAvailability();
        _overlay.RefreshCharacterScopes();
        UpdateCharacterBrowser(force: true);
        SaveSettings();
    }
    internal void RefreshCharacterManager()
    {
        if (Application.Current is not App app || Settings is null) return;
        var keys = app.ListSavedCharacters().ToList();
        var session = app.CurrentCharacterSession;
        if (session.Phase == CharacterSessionPhase.ActiveObserved && session.CharacterId is > 0)
            keys.Add(CharacterStatisticsKey.Create(session.ServerKey, session.CharacterId.Value));
        if (CharacterScopeService.Discover(Settings, keys)) RequestSave();
    }
    private string CharacterCaption(CharacterStatisticsKey key) => Settings.Characters.FirstOrDefault(c =>
        c.CharacterId == key.CharacterId && string.Equals(c.ServerProfile, key.ServerKey, StringComparison.OrdinalIgnoreCase))?.Caption
        ?? $"{key.ServerKey} · CID {key.CharacterId}";
}
