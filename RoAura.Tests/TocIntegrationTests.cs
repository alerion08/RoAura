using System.Reflection;
using System.Text.Json;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class TocIntegrationTests
{
    [Theory]
    [InlineData("ToC")]
    [InlineData("toc")]
    [InlineData("Tides of Comodo")]
    public void TocIsAnExplicitServer(string name)
    {
        Assert.Equal(GameServerProfile.Toc, GameServerProfile.Normalize(name));
        Assert.Equal(GameDatabaseVersion.Toc, GameDatabaseVersion.Normalize("ToC"));
        Assert.True(GameServerProfile.SupportsServerDatabases(name));
        Assert.True(GameServerProfile.SupportsDatabaseOverlay(name));
        Assert.True(GameServerProfile.SupportsGoldGoblin(name));
        Assert.False(GameServerProfile.SupportsMarket(name));
    }

    [Fact]
    public void PacketSwitchOnlyDisablesFeaturesWhichReadPackets()
    {
        foreach (var feature in Enum.GetValues<AppFeature>())
            Assert.Equal(!FeatureAvailability.RequiresPackets(feature),
                FeatureAvailability.CanUse(GameServerProfile.Toc, false, feature));
        Assert.True(FeatureAvailability.CanUse(GameServerProfile.Toc, true, AppFeature.Statistics));
        Assert.False(FeatureAvailability.CanUse(GameServerProfile.Other, true, AppFeature.Database));
        Assert.False(FeatureAvailability.CanUse(GameServerProfile.Rtm, true, AppFeature.GoldGoblin));
        Assert.True(FeatureAvailability.CanUse(GameServerProfile.SpringRo, false, AppFeature.MvpTimers));
    }

    [Fact]
    public void CatalogUsesTocDataAndCompleteOfflineItemDetails()
    {
        var database = new GameDatabaseService(GameDatabaseVersion.Toc);
        Assert.Equal("ToC", database.Version);
        Assert.Equal(9894, database.Items.Count);
        Assert.Equal(1061, database.Monsters.Count);
        Assert.Equal(61, database.Monsters.Count(m => m.IsMvp));
        var poring = database.Monsters.Single(m => m.Id == 1002);
        Assert.Equal("14", poring.BaseExp);
        Assert.Equal("7", poring.JobExp);
        Assert.Contains(poring.Drops, d => d.ItemId == 909 && d.Rate == "100%" && !d.IsMvp);
        Assert.All(poring.Spawns, s => Assert.Equal("—", s.Respawn));
        Assert.NotEmpty(poring.Skills);
        var potion = database.Items.Single(i => i.Id == 501);
        Assert.Equal(25, potion.NpcSellPrice);
        Assert.Contains("potion", potion.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("itemheal", potion.Stats["script"]);
        Assert.Equal("7", potion.Stats["Weight"]);
        var catalog = ItemCatalogService.Load(GameDatabaseVersion.Toc);
        Assert.Equal(9894, catalog.Count);
        Assert.Contains(catalog.Values, item => item.FullName.Contains("[3]"));
        Assert.Equal(61, MonsterCatalogService.Load(GameDatabaseVersion.Toc).Count);
        database.Reload(GameDatabaseVersion.SpringRo);
        Assert.Equal(GameDatabaseVersion.SpringRo, database.Version);
        Assert.DoesNotContain(database.Items, i => i.ImageUrl.StartsWith("https://tidesofcomodo.com/"));
    }

    [Fact]
    public void ServerSpecificTimersRulesAndOffSwitchSurviveSettingsAndPresets()
    {
        var settings = new AppSettings { ServerProfile = "ToC", PacketCaptureEnabled = false };
        settings.MvpTimers.Add(new MvpTimer { Name = "Spring timer", MonsterId = 1039 });
        settings.ActiveMvpTimers.Add(new MvpTimer { Name = "ToC timer", MonsterId = 1038, DurationMinutes = 120 });
        settings.GoldGoblinPriceRules.Add(new GoldGoblinPriceRule { ItemId = 501, ManualPrice = 999 });
        settings.ActiveGoldGoblinPriceRules.Add(new GoldGoblinPriceRule { ItemId = 501, ManualPrice = 77 });
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.False(restored.PacketCaptureEnabled);
        Assert.Equal("ToC timer", Assert.Single(restored.ActiveMvpTimers).Name);
        Assert.Equal(77, Assert.Single(restored.ActiveGoldGoblinPriceRules).ManualPrice);
        var live = new AppSettings();
        var timerReference = live.TocMvpTimers;
        SettingsSnapshot.Apply(restored, live);
        Assert.Same(timerReference, live.TocMvpTimers);
        Assert.Equal("ToC timer", Assert.Single(live.ActiveMvpTimers).Name);
        live.ServerProfile = GameServerProfile.SpringRo;
        Assert.Equal("Spring timer", Assert.Single(live.ActiveMvpTimers).Name);
        Assert.Equal(999, Assert.Single(live.ActiveGoldGoblinPriceRules).ManualPrice);
        Assert.False(live.PacketCaptureEnabled);
        Assert.NotEqual(CharacterStatisticsKey.Create("ToC", 123), CharacterStatisticsKey.Create("SpringRO", 123));
    }

    [Fact]
    public void WorkingSettingsKeepTocCollectionsAndPacketChoiceOnDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoAura-ToC-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(root);
            var settings = store.Load();
            settings.ServerProfile = "ToC";
            settings.PacketCaptureEnabled = false;
            settings.TocMvpTimers.Add(new MvpTimer { Name = "ToC", MonsterId = 1039, DurationMinutes = 120 });
            settings.TocGoldGoblinPriceRules.Add(new GoldGoblinPriceRule { ItemId = 501, ManualPrice = 77 });
            settings.MvpTimers.Add(new MvpTimer { Name = "Legacy" });
            store.Save(settings);
            var restored = new SettingsStore(root).Load();
            Assert.Equal(12, restored.SettingsVersion);
            Assert.Equal("ToC", restored.ServerProfile);
            Assert.False(restored.PacketCaptureEnabled);
            Assert.Equal(1039, Assert.Single(restored.ActiveMvpTimers).MonsterId);
            Assert.Equal("Legacy", Assert.Single(restored.MvpTimers).Name);
            Assert.Equal(77, Assert.Single(restored.ActiveGoldGoblinPriceRules).ManualPrice);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GoldGoblinUsesTocNpcPricesAndIgnoresSpringRules()
    {
        var settings = new AppSettings { ServerProfile = "ToC" };
        settings.GoldGoblinPriceRules.Add(new GoldGoblinPriceRule { ItemId = 501, ManualPrice = 999 });
        var service = new GoldGoblinService(settings, new SpringRoMarketService());
        Assert.Equal((25L, "NPC", true), service.ResolvePriceInfo(501, "Red Potion"));
        settings.GoldGoblinOverchargeLevel = 10;
        Assert.Equal(31, service.ResolveUnitPrice(501, "Red Potion"));
        settings.TocGoldGoblinPriceRules.Add(new GoldGoblinPriceRule { ItemId = 501, ManualPrice = 77, UseVendingPrice = true });
        Assert.Equal((77L, "Manual", true), service.ResolvePriceInfo(501, "Red Potion"));
        settings.ServerProfile = GameServerProfile.SpringRo;
        Assert.Equal(999, service.ResolveUnitPrice(501, "Red Potion"));
    }

    [Fact]
    public void OffSwitchCannotCreateSpringClientReaderOrCaptureWorker()
    {
        using var capture = new NpcapPacketCapture();
        capture.ApplySettings(new AppSettings { ServerProfile = GameServerProfile.SpringRo, PacketCaptureEnabled = false });
        Assert.False(capture.IsActive);
        Assert.Null(typeof(NpcapPacketCapture).GetField("_clientStatusBridge", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(capture));
        Assert.Null(typeof(NpcapPacketCapture).GetField("_cancellation", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(capture));
        capture.ApplySettings(new AppSettings { ServerProfile = GameServerProfile.Toc, PacketCaptureEnabled = false });
        Assert.False(capture.IsActive);
        Assert.Null(typeof(NpcapPacketCapture).GetField("_clientStatusBridge", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(capture));
    }
}
