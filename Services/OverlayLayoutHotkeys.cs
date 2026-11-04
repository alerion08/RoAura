using RoAura.Models;
namespace RoAura.Services;

public static class OverlayLayoutHotkeys
{
    public static bool Enabled(AppSettings settings) => HotkeyChord.TryParse(settings.InterfaceLayoutHotkey, out _) &&
        !new[] { settings.SettingsHotkey, settings.OverlaysHotkey, settings.FinishMoveHotkey, settings.TimersHotkey,
            settings.DatabaseHotkey, settings.QuestBrowserHotkey, settings.QuestTrackerHotkey,
            settings.StartSessionHotkey, settings.PauseSessionHotkey, settings.AddMvpTimerHotkey }
            .Any(key => HotkeyChord.Canonical(key) == HotkeyChord.Canonical(settings.InterfaceLayoutHotkey));
}
