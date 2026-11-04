using System.Text.Json;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class FarmingSessionActionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shared_boundary_clears_all_session_data_and_keeps_lifetime_progress_and_rules(bool startImmediately)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoAura-session-actions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppSettings { ServerProfile = "SpringRO" };
            settings.GoldGoblinPriceRules.Add(new() { ItemId = 501, Name = "Potion", ManualPrice = 50 });
            var gold = new GoldGoblinService(settings, new SpringRoMarketService(cacheDirectory: directory));
            var store = new CharacterStatisticsStore(directory);
            var ledger = new CharacterStatisticsLedger(store);
            var key = CharacterStatisticsKey.Create("SpringRO", 123);
            var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
            ledger.SwitchTo(key, now);
            ledger.AddExperience(100, false, now);
            ledger.AddExperience(25, true, now);
            ledger.SetProgress(false, 500, 1000, now);
            ledger.AddLoot(501, "Potion", 3, now);
            ledger.ObserveZeny(1000, now);
            ledger.ObserveZeny(1200, now);
            gold.AddLoot(501, "Potion", 3);
            gold.Rows[0].Cost = 77;
            ledger.ReplaceGoldGoblinRows(gold.SnapshotCharacterRows(), now);
            ledger.SetPaused(true, now.AddMinutes(2));
            settings.SessionPaused = true;
            var previous = ledger.Snapshot(key, now.AddMinutes(2))!;

            FarmingSessionActions.Restart(ledger, gold, settings, now.AddMinutes(3), startImmediately);

            var current = ledger.Snapshot(key, now.AddMinutes(3))!;
            Assert.NotEqual(previous.SessionId, current.SessionId);
            Assert.Equal(0, current.SessionBaseExperience);
            Assert.Equal(0, current.SessionJobExperience);
            Assert.Equal(0, current.SessionZenyGained);
            Assert.Equal(0, current.SessionActiveTicks);
            Assert.Empty(current.SessionLoot);
            Assert.Empty(current.GoldGoblinRows);
            Assert.Empty(gold.Rows);
            Assert.Equal(0, gold.SessionTotal);
            Assert.Equal(startImmediately, gold.SessionStartedUtc.HasValue);
            Assert.Equal(startImmediately, current.SessionTimerStarted);
            Assert.False(current.SessionPaused);
            Assert.False(settings.SessionPaused);
            Assert.Equal(100, current.LifetimeBaseExperience);
            Assert.Equal(25, current.LifetimeJobExperience);
            Assert.Equal(200, current.LifetimeZenyGained);
            Assert.Equal(3, current.LifetimeLoot[501].Count);
            Assert.Equal(500UL, current.CurrentBaseExperience);
            Assert.Equal(50, Assert.Single(settings.GoldGoblinPriceRules).ManualPrice);
            Assert.Equal(startImmediately ? TimeSpan.FromMinutes(1) : TimeSpan.Zero,
                ledger.ActiveSessionElapsed(now.AddMinutes(4)));
            var persisted = new CharacterStatisticsLedger(store);
            persisted.SwitchTo(key, now.AddHours(1));
            Assert.Empty(persisted.Snapshot(key, now.AddHours(1))!.GoldGoblinRows);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false, false, 200)]
    [InlineData(false, true, 350)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 150)]
    public void Direct_income_can_be_excluded_independently_of_loot_valuation(bool ignore, bool includeLoot, long expected)
    {
        var settings = new AppSettings { ServerProfile = "SpringRO", GoldGoblinIgnoreZenyIncome = ignore,
            IncludeGoldGoblinInZenyPerHour = includeLoot };
        Assert.Equal(expected, FarmingSessionActions.CalculateIncome(settings, 200, 150));
        settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(ignore, settings.GoldGoblinIgnoreZenyIncome);
        Assert.Equal(expected, FarmingSessionActions.CalculateIncome(settings, 200, 150));
        settings.ServerProfile = "RTM";
        Assert.Equal(200, FarmingSessionActions.CalculateIncome(settings, 200, 150));
    }

    [Fact]
    public void Start_hotkey_is_unassigned_by_default_and_conflicts_do_not_disable_unrelated_actions()
    {
        var settings = new AppSettings();
        Assert.Equal("None", settings.StartSessionHotkey);
        Assert.False(settings.HotkeyConflicts);
        Assert.False(settings.FarmingHotkeyConflict);
        settings.StartSessionHotkey = "Ctrl+F3";
        Assert.True(FarmingHotkeys.Enabled(settings, settings.StartSessionHotkey));
        settings.PauseSessionHotkey = "Control+F3";
        Assert.True(settings.HotkeyConflicts);
        Assert.False(FarmingHotkeys.Enabled(settings, settings.StartSessionHotkey));
        Assert.False(FarmingHotkeys.Enabled(settings, settings.PauseSessionHotkey));
        Assert.True(FarmingHotkeys.Enabled(settings, settings.AddMvpTimerHotkey));
        settings.StartSessionHotkey = "None";
        Assert.False(settings.HotkeyConflicts);
        settings.PauseSessionHotkey = "None";
        settings.AddMvpTimerHotkey = "None";
        Assert.False(settings.FarmingHotkeyConflict);
    }
}
