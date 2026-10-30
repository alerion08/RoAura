using RoAura.Models;
namespace RoAura.Services;
public static class QuestHotkeys
{
    private static string Canonical(string value) => value.Trim().Equals("Esc",StringComparison.OrdinalIgnoreCase) ? "ESCAPE" : value.Trim().ToUpperInvariant();
    public static (bool Browser, bool Tracker) Enabled(AppSettings settings)
    {
        var old = new[] { settings.SettingsHotkey,settings.OverlaysHotkey,settings.FinishMoveHotkey,settings.TimersHotkey,settings.DatabaseHotkey }.Select(Canonical).ToHashSet();
        var browser=Canonical(settings.QuestBrowserHotkey);var tracker=Canonical(settings.QuestTrackerHotkey);
        return (!old.Contains(browser) && browser!=tracker, !old.Contains(tracker) && browser!=tracker);
    }
}
