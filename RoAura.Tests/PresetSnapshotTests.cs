using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class PresetSnapshotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoAura.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SwitchFullSnapshotsInPlaceRepeatedly_AndSaveNewWorkingState()
    {
        var store = new SettingsStore(_root);
        var live = store.Load();
        live.Enabled = false;
        live.AllowedProcesses = ["FirstClient"];
        live.Skills.Add(new SkillAura { Name = "First skill", X = 123, Y = 234 });
        live.Effects.Add(new TrackedEffect { Name = "First effect", NotificationX = 345, ShowOnBuffPanel = true, GroupNumber = 1 });
        live.Reminders.Add(new ReminderItem { Name = "First reminder", X = 456 });
        live.Equipment.Add(new EquipmentTracker { Name = "First weapon", ItemId = 1148, VariantKey = "cards-refine", X = 567 });
        live.MvpTimers.Add(new MvpTimer { Name = "Custom MVP", MonsterId = 1086 });
        live.GoldGoblinPriceRules.Add(new GoldGoblinPriceRule { ItemId = 730, Name = "Diamond", ManualPrice = 999 });
        live.GoldGoblinOverchargeLevel = 8;
        live.MarketWatchRules.Add(new MarketWatchRule { ItemId = 730, ItemName = "Diamond", MaximumPrice = 5000 });
        live.MarketWatchNotifications.X = 876;
        live.MarketWatchNotifications.TextColor = "#AB123456";
        live.TimersX = 765;
        live.RateIndicators[0].X = 654;
        live.SyncBuffPanelGroups();
        live.BuffPanelGroups[0].X = 543;
        store.CreateProfile("Knight", live);
        var first = File.ReadAllText(Path.Combine(store.ProfilesDirectory, "Knight.json"));

        var second = new SettingsStore(Path.Combine(_root, "second")).Load();
        second.SettingsHotkey = "F4";
        second.Skills.Add(new SkillAura { Name = "Second skill", X = 2000 });
        second.MarketWatchNotifications.X = 2222;
        store.CreateProfile("Wizard", second);
        var secondJson = File.ReadAllText(Path.Combine(store.ProfilesDirectory, "Wizard.json"));
        var collections = typeof(AppSettings).GetProperties().Where(p => p.GetValue(live) is IList)
            .ToDictionary(p => p, p => p.GetValue(live));
        var notifications = live.MarketWatchNotifications;
        for (var i = 0; i < 3; i++)
        {
            store.ApplyProfileTo("Wizard", live);
            Assert.Equal("Second skill", Assert.Single(live.Skills).Name);
            Assert.Empty(live.Equipment);
            Assert.Equal(2222, live.MarketWatchNotifications.X);
            Assert.Equal("F4", live.SettingsHotkey);
            store.Save(live);
            Assert.Equal(2222, store.Load().MarketWatchNotifications.X);
            store.ApplyProfileTo("Knight", live);
            Assert.Equal("First skill", Assert.Single(live.Skills).Name);
            Assert.Equal("cards-refine", Assert.Single(live.Equipment).VariantKey);
            Assert.Equal(876, live.MarketWatchNotifications.X);
            Assert.Equal(654, live.RateIndicators[0].X);
            Assert.False(live.Enabled);
            Assert.False(store.ExitSaveSuppressed);
            foreach (var pair in collections) Assert.Same(pair.Value, pair.Key.GetValue(live));
            Assert.Same(notifications, live.MarketWatchNotifications);
        }
        store.Save(live);
        // Compare every serialized setting, including groups and future added fields.
        var expected = JsonSerializer.Deserialize<AppSettings>(first, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } })!;
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(store.Load()));
        Assert.Equal(first, File.ReadAllText(Path.Combine(store.ProfilesDirectory, "Knight.json")));
        Assert.Equal(secondJson, File.ReadAllText(Path.Combine(store.ProfilesDirectory, "Wizard.json")));
    }

    [Fact]
    public void CorruptPreset_DoesNotChangeLiveSettingsOrActiveName()
    {
        var store = new SettingsStore(_root);
        var live = store.Load();
        live.TimersX = 1789;
        store.Save(live);
        var original = File.ReadAllText(store.SettingsPath);
        File.WriteAllText(Path.Combine(store.ProfilesDirectory, "Broken.json"), "{ broken");
        Assert.Throws<JsonException>(() => store.ApplyProfileTo("Broken", live));
        Assert.Equal(1789, live.TimersX);
        Assert.Equal("Default", store.ActiveProfileName);
        Assert.Equal(original, File.ReadAllText(store.SettingsPath));
    }

    [Theory]
    [InlineData("#123456", true, "#FF123456")]
    [InlineData("#00123456", true, "#00123456")]
    [InlineData("#80123456", true, "#80123456")]
    [InlineData("#12345", false, "")]
    [InlineData("#GG123456", false, "")]
    public void PaletteHexSupportsAlphaAndRejectsMalformedInput(string hex, bool valid, string expected)
    {
        Assert.Equal(valid, ColorPickerWindow.TryParseHex(hex, out var color));
        if (valid) Assert.Equal(expected, color.ToString());
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
