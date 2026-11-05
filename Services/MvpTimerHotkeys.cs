using RoAura.Models;

namespace RoAura.Services;

public static class MvpTimerHotkeys
{
    public const int Maximum = 12;
    public static IReadOnlyList<string> Keys(AppSettings settings) =>
    [
        settings.RestartMvpTimer1Hotkey, settings.RestartMvpTimer2Hotkey,
        settings.RestartMvpTimer3Hotkey, settings.RestartMvpTimer4Hotkey,
        settings.RestartMvpTimer5Hotkey, settings.RestartMvpTimer6Hotkey,
        settings.RestartMvpTimer7Hotkey, settings.RestartMvpTimer8Hotkey,
        settings.RestartMvpTimer9Hotkey, settings.RestartMvpTimer10Hotkey,
        settings.RestartMvpTimer11Hotkey, settings.RestartMvpTimer12Hotkey
    ];

    public static bool Enabled(AppSettings settings, int number)
    {
        if (number is < 1 or > Maximum) return false;
        var keys = Keys(settings);
        if (!HotkeyChord.TryParse(keys[number - 1], out var chord)) return false;
        var existing = new[] { settings.SettingsHotkey, settings.OverlaysHotkey, settings.FinishMoveHotkey,
            settings.TimersHotkey, settings.DatabaseHotkey, settings.QuestBrowserHotkey, settings.QuestTrackerHotkey,
            settings.StartSessionHotkey, settings.PauseSessionHotkey, settings.AddMvpTimerHotkey, settings.InterfaceLayoutHotkey };
        return !existing.Any(key => HotkeyChord.TryParse(key, out var other) && other == chord) &&
            keys.Count(key => HotkeyChord.TryParse(key, out var other) && other == chord) == 1;
    }

    public static MvpTimer? Resolve(AppSettings settings, int number) =>
        number is >= 1 and <= Maximum ? settings.ActiveMvpTimers.ElementAtOrDefault(number - 1) : null;

    // Every configured row occupies its number, including stopped/disabled rows.
    // Visibility and countdown completion must not retarget a shortcut.
    public static void RefreshNumbers(IReadOnlyList<MvpTimer> timers)
    {
        for (var index = 0; index < timers.Count; index++) timers[index].DisplayNumber = index + 1;
    }
}
