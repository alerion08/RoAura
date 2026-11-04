using RoAura.Models;
namespace RoAura.Services;

public static class FarmingHotkeys
{
    private static string Canonical(string key) => HotkeyChord.Canonical(key);
    public static bool Enabled(AppSettings settings, string key)
    {
        if (!HotkeyChord.TryParse(key, out _)) return false;
        var value = Canonical(key);
        var existing = new[] { settings.SettingsHotkey, settings.OverlaysHotkey, settings.FinishMoveHotkey,
            settings.TimersHotkey, settings.DatabaseHotkey, settings.QuestBrowserHotkey, settings.QuestTrackerHotkey };
        return value.Length > 0 &&
            !existing.Any(old => Canonical(old) == value) &&
            new[] { settings.StartSessionHotkey, settings.PauseSessionHotkey, settings.AddMvpTimerHotkey }
                .Count(candidate => Canonical(candidate) == value) == 1;
    }
    public static bool HasConflict(AppSettings settings) =>
        new[] { settings.StartSessionHotkey, settings.PauseSessionHotkey, settings.AddMvpTimerHotkey }
            .Any(key => HotkeyChord.TryParse(key, out _) && !Enabled(settings, key));
}
