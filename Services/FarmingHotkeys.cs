using RoAura.Models;
namespace RoAura.Services;

public static class FarmingHotkeys
{
    private static string Canonical(string key) => HotkeyChord.Canonical(key);
    public static bool Enabled(AppSettings settings, string key)
    {
        var value = Canonical(key);
        var existing = new[] { settings.SettingsHotkey, settings.OverlaysHotkey, settings.FinishMoveHotkey,
            settings.TimersHotkey, settings.DatabaseHotkey, settings.QuestBrowserHotkey, settings.QuestTrackerHotkey };
        return value.Length > 0 &&
            !existing.Any(old => Canonical(old) == value) &&
            Canonical(settings.PauseSessionHotkey) != Canonical(settings.AddMvpTimerHotkey);
    }
    public static bool HasConflict(AppSettings settings) =>
        !Enabled(settings, settings.PauseSessionHotkey) || !Enabled(settings, settings.AddMvpTimerHotkey);
}
