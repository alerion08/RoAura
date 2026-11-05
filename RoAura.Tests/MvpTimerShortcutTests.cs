using System.Text.Json;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class MvpTimerShortcutTests
{
    [Fact]
    public void Twelve_optional_shortcuts_default_to_none_and_report_changes()
    {
        var settings = new AppSettings();
        Assert.Equal(12, MvpTimerHotkeys.Keys(settings).Count);
        Assert.All(MvpTimerHotkeys.Keys(settings), key => Assert.Equal("None", key));
        Assert.False(settings.HotkeyConflicts);
        var changes = new List<string?>(); settings.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        settings.RestartMvpTimer12Hotkey = "Ctrl+F12";
        Assert.True(MvpTimerHotkeys.Enabled(settings, 12));
        Assert.Contains(nameof(AppSettings.HotkeyConflicts), changes);
        Assert.Contains(nameof(AppSettings.RestartMvpTimer12Hotkey), changes);
        Assert.False(MvpTimerHotkeys.Enabled(settings, 0)); Assert.False(MvpTimerHotkeys.Enabled(settings, 13));
    }

    [Fact]
    public void Existing_actions_keep_priority_and_duplicate_indexed_shortcuts_are_disabled()
    {
        var settings = new AppSettings { RestartMvpTimer1Hotkey = "F8" };
        Assert.False(MvpTimerHotkeys.Enabled(settings, 1)); Assert.True(settings.HotkeyConflicts);
        settings.RestartMvpTimer1Hotkey = "Ctrl+F11"; settings.RestartMvpTimer2Hotkey = "ctrl+f11";
        Assert.False(MvpTimerHotkeys.Enabled(settings, 1)); Assert.False(MvpTimerHotkeys.Enabled(settings, 2));
        settings.RestartMvpTimer2Hotkey = "None";
        Assert.True(MvpTimerHotkeys.Enabled(settings, 1)); Assert.False(settings.HotkeyConflicts);
        settings.InterfaceLayoutHotkey = "Ctrl+F11";
        Assert.False(MvpTimerHotkeys.Enabled(settings, 1));
    }

    [Fact]
    public void Shortcuts_persist_on_disk_and_presets_restore_cleared_slots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoAura-mvp-shortcuts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppSettings { RestartMvpTimer1Hotkey = "Ctrl+F1", RestartMvpTimer12Hotkey = "Alt+F12" };
            var store = new SettingsStore(directory); store.Save(settings);
            var restored = new SettingsStore(directory).Load();
            Assert.Equal("Ctrl+F1", restored.RestartMvpTimer1Hotkey);
            Assert.Equal("Alt+F12", restored.RestartMvpTimer12Hotkey);
            var live = new AppSettings(); SettingsSnapshot.Apply(restored, live);
            Assert.Equal(MvpTimerHotkeys.Keys(restored), MvpTimerHotkeys.Keys(live));
            restored.RestartMvpTimer12Hotkey = "None"; SettingsSnapshot.Apply(restored, live);
            Assert.False(MvpTimerHotkeys.Enabled(live, 12));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Numbers_follow_the_full_list_and_do_not_shift_when_a_timer_stops_or_is_hidden()
    {
        var settings = new AppSettings();
        for (var number = 1; number <= 14; number++) settings.MvpTimers.Add(new MvpTimer { Name = $"Boss {number}" });
        settings.MvpTimers[0].Enabled = false; settings.MvpTimers[1].Reset();
        MvpTimerHotkeys.RefreshNumbers(settings.ActiveMvpTimers);
        Assert.Equal("12. Boss 12", settings.MvpTimers[11].NumberedName);
        Assert.Same(settings.MvpTimers[0], MvpTimerHotkeys.Resolve(settings, 1));
        Assert.Same(settings.MvpTimers[11], MvpTimerHotkeys.Resolve(settings, 12));
        Assert.Null(MvpTimerHotkeys.Resolve(settings, 13));
        settings.MvpTimers.RemoveAt(0); MvpTimerHotkeys.RefreshNumbers(settings.ActiveMvpTimers);
        Assert.Equal("1. Boss 2", settings.MvpTimers[0].NumberedName);
        Assert.DoesNotContain("DisplayNumber", JsonSerializer.Serialize(settings.MvpTimers[0]));
    }

    [Fact]
    public void Slot_resolution_uses_the_current_profile_collection()
    {
        var settings = new AppSettings();
        var legacy = new MvpTimer { Name = "Legacy" }; var toc = new MvpTimer { Name = "ToC" };
        settings.MvpTimers.Add(legacy); settings.TocMvpTimers.Add(toc);
        Assert.Same(legacy, MvpTimerHotkeys.Resolve(settings, 1));
        settings.ServerProfile = "ToC"; Assert.Same(toc, MvpTimerHotkeys.Resolve(settings, 1));
        settings.ServerProfile = "MuhRO"; Assert.Same(legacy, MvpTimerHotkeys.Resolve(settings, 1));
        Assert.Null(MvpTimerHotkeys.Resolve(settings, 12));
    }

    [Fact]
    public void Restart_draft_keeps_all_timer_configuration_and_does_not_mutate_the_live_timer()
    {
        var timer = Timer(); timer.Enabled = false; var before = JsonSerializer.Serialize(timer);
        var keyTime = DateTime.UtcNow;
        var draft = timer.CreateRestartDraft(keyTime);
        Assert.Equal(before, JsonSerializer.Serialize(timer));
        Assert.Equal(timer.Id, draft.Id); Assert.Equal(timer.Name, draft.Name);
        Assert.Equal(timer.MonsterId, draft.MonsterId); Assert.Equal(timer.DurationMinutes, draft.DurationMinutes);
        Assert.Equal(timer.Enabled, draft.Enabled); Assert.Equal(timer.LocationCode, draft.LocationCode);
        Assert.Equal(timer.CompletionSoundEnabled, draft.CompletionSoundEnabled);
        Assert.Equal(timer.CompletionSoundPath, draft.CompletionSoundPath);
        Assert.Equal(timer.CompletionSoundVolumePercent, draft.CompletionSoundVolumePercent);
        Assert.Equal(timer.RespawnHints, draft.RespawnHints); Assert.Equal(timer.MonsterImagePath, draft.MonsterImagePath);
        Assert.Null(draft.MarkerX); Assert.Null(draft.MarkerY);
        Assert.Equal(keyTime, draft.KilledAtUtc); Assert.Equal(keyTime.AddMinutes(43), draft.EndsAtUtc);
    }

    [Fact]
    public void Confirm_updates_the_same_timer_with_the_new_marker_and_original_keypress_time()
    {
        var timer = Timer(); var id = timer.Id;
        var keyTime = DateTime.UtcNow.AddSeconds(-30); var draft = timer.CreateRestartDraft(keyTime);
        draft.MarkerX = .7; draft.MarkerY = .8; timer.ApplyRestartDraft(draft);
        Assert.Equal(id, timer.Id); Assert.Equal(.7, timer.MarkerX); Assert.Equal(.8, timer.MarkerY);
        Assert.Equal(keyTime, timer.KilledAtUtc); Assert.Equal(keyTime.AddMinutes(43), timer.EndsAtUtc);
        Assert.InRange(timer.RemainingSeconds, 43 * 60 - 32, 43 * 60 - 30);
        Assert.False(timer.CompletionSoundPlayed);
    }

    [Fact]
    public void Restart_preserves_the_complete_timestamp_across_midnight_and_rejects_another_timer()
    {
        var timer = Timer(); var midnight = new DateTime(2026, 11, 4, 23, 59, 58, DateTimeKind.Utc);
        var draft = timer.CreateRestartDraft(midnight); timer.ApplyRestartDraft(draft);
        Assert.Equal(midnight, timer.KilledAtUtc); Assert.Equal(midnight.AddMinutes(43), timer.EndsAtUtc);
        var before = JsonSerializer.Serialize(timer); draft.Id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => timer.ApplyRestartDraft(draft));
        Assert.Equal(before, JsonSerializer.Serialize(timer));
    }

    [Theory]
    [InlineData("RTM")][InlineData("SpringRO")][InlineData("ToC")][InlineData("MuhRO")][InlineData("Other")]
    public void Statistics_require_packets_but_no_server_database_and_manual_timers_remain_available(string server)
    {
        Assert.True(FeatureAvailability.CanUse(server, true, AppFeature.Statistics));
        Assert.False(FeatureAvailability.CanUse(server, false, AppFeature.Statistics));
        Assert.True(FeatureAvailability.CanUse(server, true, AppFeature.MvpTimers));
        Assert.True(FeatureAvailability.CanUse(server, false, AppFeature.MvpTimers));
    }

    private static MvpTimer Timer()
    {
        var timer = new MvpTimer { Name = "Custom boss", MonsterId = 1039, DurationMinutes = 43, Enabled = true,
            LocationCode = "second", MarkerX = .2, MarkerY = .3,
            CompletionSoundEnabled = true, CompletionSoundPath = "custom.wav", CompletionSoundVolumePercent = 37 };
        timer.SetMonsterDetails(new MonsterEntry { Id = 1039, ImageUrl = "https://example.test/boss.png",
            Spawns = [new() { Code = "first" }, new() { Code = "second" }] });
        timer.StartFromKillTime(DateTime.UtcNow.AddMinutes(-20)); timer.CompletionSoundPlayed = true;
        return timer;
    }
}
