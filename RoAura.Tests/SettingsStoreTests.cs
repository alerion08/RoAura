using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoAura.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SeparateProfilesKeepIndependentEquipmentSnapshots()
    {
        var store = new SettingsStore(_root);
        var settings = store.Load();
        settings.Equipment.Add(new EquipmentTracker
        {
            Name = "+7 Triple Kill Word of Misery",
            ItemId = 1148,
            InventoryIndex = 10,
            VariantKey = "weapon-with-three-cards"
        });
        store.CreateProfile("Equipment", settings);

        settings.Equipment.Clear();
        store.Save(settings);
        store.CreateProfile("Empty", settings);

        store.ApplyProfile("Equipment");
        var restored = store.Load();

        var item = Assert.Single(restored.Equipment);
        Assert.Equal(1148, item.ItemId);
        Assert.Equal("weapon-with-three-cards", item.VariantKey);
        Assert.Equal("+7 Triple Kill Word of Misery", item.Name);
    }

    [Fact]
    public void SavingWorkingCopyDoesNotModifyStoredProfile()
    {
        var store = new SettingsStore(_root);
        var settings = store.Load();
        settings.PacketServerPort = 5121;
        store.CreateProfile("Server A", settings);

        settings.PacketServerPort = 64906;
        store.Save(settings);
        store.ApplyProfile("Server A");

        Assert.Equal(5121, store.Load().PacketServerPort);
    }

    [Fact]
    public void UpdatingProfileCreatesBackup()
    {
        var store = new SettingsStore(_root);
        var settings = store.Load();
        store.CreateProfile("Backup test", settings);
        settings.PacketServerPort = 6000;

        store.UpdateProfile("Backup test", settings);

        Assert.True(Directory.Exists(store.ProfileBackupsDirectory));
        Assert.NotEmpty(Directory.EnumerateFiles(store.ProfileBackupsDirectory, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void InvalidWorkingJsonIsNotOverwrittenDuringLoad()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        const string invalidJson = "{ definitely not valid json";
        File.WriteAllText(settingsPath, invalidJson);

        _ = new SettingsStore(_root).Load();

        Assert.Equal(invalidJson, File.ReadAllText(settingsPath));
    }

    [Fact]
    public void CorruptWorkingJsonIsPreservedUntilDefaultsAreAccepted()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        const string invalidJson = "{ definitely not valid json";
        File.WriteAllText(settingsPath, invalidJson);
        var store = new SettingsStore(_root);

        var settings = store.Load();

        Assert.True(store.LoadFailed);
        Assert.False(string.IsNullOrWhiteSpace(store.LoadError));
        Assert.NotNull(store.LoadRecoveryPath);
        Assert.True(File.Exists(store.LoadRecoveryPath));
        Assert.Equal(invalidJson, File.ReadAllText(store.LoadRecoveryPath!));
        Assert.NotEmpty(Directory.EnumerateFiles(store.RecoveryDirectory, "settings.corrupt.*.json"));

        store.Save(settings);
        Assert.Equal(invalidJson, File.ReadAllText(settingsPath));

        store.AcceptDefaults();
        Assert.False(store.LoadFailed);
        store.Save(settings);
        Assert.NotEqual(invalidJson, File.ReadAllText(settingsPath));
        Assert.Equal(11, new SettingsStore(_root).Load().SettingsVersion);
    }

    [Fact]
    public void NullCollectionsInWorkingJsonAreReplacedWithEmptyOnes()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "settings.json"), """
        {
          "SettingsVersion": 11,
          "Skills": null,
          "Effects": null,
          "RewardChannels": null,
          "RateIndicators": null,
          "AllowedProcesses": null
        }
        """);
        var store = new SettingsStore(_root);

        var settings = store.Load();

        Assert.False(store.LoadFailed);
        Assert.NotNull(settings.Skills);
        Assert.Empty(settings.Skills);
        Assert.NotNull(settings.Effects);
        Assert.NotNull(settings.AllowedProcesses);
        Assert.Equal(3, settings.RewardChannels.Count);
        Assert.Equal(6, settings.RateIndicators.Count);
    }

    [Fact]
    public void NewerSettingsVersionIsCopiedBeforeMigration()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "settings.json"), """{ "SettingsVersion": 99, "PacketServerPort": 4242 }""");
        var store = new SettingsStore(_root);

        var settings = store.Load();

        Assert.False(store.LoadFailed);
        Assert.Equal(11, settings.SettingsVersion);
        Assert.Equal(4242, settings.PacketServerPort);
        var copy = Assert.Single(Directory.EnumerateFiles(store.RecoveryDirectory, "settings.newer.*.json"));
        Assert.Contains("\"SettingsVersion\": 99", File.ReadAllText(copy));
    }

    [Fact]
    public void EnumsAreStoredAsNamesAndLegacyIntegersStillLoad()
    {
        var store = new SettingsStore(_root);
        store.Save(store.Load());
        Assert.Contains("\"Kind\": \"Loot\"", File.ReadAllText(store.SettingsPath));

        File.WriteAllText(store.SettingsPath, """
        {
          "SettingsVersion": 11,
          "RewardChannels": [{ "Kind": 2, "GroupNumber": 3 }],
        }
        """);
        var restored = new SettingsStore(_root).Load();
        var experience = Assert.Single(restored.RewardChannels.Where(x => x.Kind == RewardKind.Experience));
        Assert.Equal(3, experience.GroupNumber);
    }

    [Fact]
    public void ApplyingProfileSuppressesExitSave()
    {
        var store = new SettingsStore(_root);
        var settings = store.Load();
        store.CreateProfile("Preset", settings);
        Assert.False(store.ExitSaveSuppressed);

        store.ApplyProfile("Preset");

        Assert.True(store.ExitSaveSuppressed);
    }

    [Fact]
    public void FirstRunGuideIsOnlyPendingForNewInstallation()
    {
        var store = new SettingsStore(_root);
        _ = store.Load();
        Assert.True(store.IsFirstRun);

        store.CompleteFirstRun();
        Assert.False(store.IsFirstRun);

        var restarted = new SettingsStore(_root);
        _ = restarted.Load();
        Assert.False(restarted.IsFirstRun);
    }

    [Fact]
    public void CompleteConfigurationRoundTripPreservesUserSettings()
    {
        var store = new SettingsStore(_root);
        var settings = new AppSettings
        {
            SettingsVersion = 11,
            OverlayCoordinatesRelativeToGame = true,
            ServerProfile = GameServerProfile.SpringRo,
            Language = "en",
            SettingsHotkey = "Home",
            OverlaysHotkey = "End",
            FinishMoveHotkey = "Escape",
            TimersHotkey = "PageUp",
            DatabaseHotkey = "F6",
            NotificationX = 321.5,
            NotificationY = 654.25,
            NotificationSize = 88,
            NotificationGap = 13,
            TimersX = 777,
            TimersY = 222,
            TimersTextSize = 31,
            TimersGap = 9,
            TimersTextColor = "#FFAABBCC",
            MarketBackgroundRefreshMinutes = 17,
            RateStatisticsRefreshSeconds = 12,
            GoldGoblinOverchargeLevel = 10,
            ShowLootZenyPrice = true,
            IncludeGoldGoblinInZenyPerHour = true
        };

        settings.Skills.Add(new SkillAura
        {
            Name = "Bowling Bash", PacketSkillId = 62, CooldownSeconds = 4.5,
            GroupNumber = 3, X = 115, Y = 226, IndividualIconSize = 61,
            IconPadding = 2, ReadyOpacityPercent = 91, CooldownOpacityPercent = 37,
            ShortNameOverride = "BB", ShowCountdown = false, IconImageBase64 = "AQID"
        });
        settings.CooldownGroups.Add(new CooldownGroupSettings
            { GroupNumber = 3, Name = "Damage", Direction = "Vertical", Gap = 11 });
        settings.Effects.Add(new TrackedEffect
        {
            Name = "Assumptio", StatusId = 110, GroupNumber = 4,
            NotificationX = 410, NotificationY = 125, NotificationIconSize = 52,
            NotificationIconPadding = 3, NotificationTextSize = 19,
            NotificationOpacityPercent = 83, AppearedSoundEnabled = true,
            AppearedSoundPath = "appear.wav", AppearedSoundVolumePercent = 72,
            DisappearedSoundEnabled = true, DisappearedSoundPath = "end.wav",
            DisappearedSoundVolumePercent = 43, ShowOnBuffPanel = true,
            ShowDurationText = false, ShowDurationFill = true
        });
        settings.BuffPanelGroups.Add(new BuffPanelGroup
        {
            GroupNumber = 4, X = 444, Y = 555, IconSize = 47, IconPadding = 1,
            Gap = 7, ActiveOpacityPercent = 96, InactiveOpacityPercent = 28
        });
        settings.Reminders.Add(new ReminderItem
        {
            Name = "Missing food", StatusId = 203, GroupNumber = 5,
            X = 606, Y = 707, IconSize = 69, IconPadding = 4, TextSize = 17,
            OpacityPercent = 76, ShowIcon = false, Text = "USE FOOD",
            ShowBackground = false, ShowBorder = true
        });
        settings.Equipment.Add(new EquipmentTracker
        {
            Name = "+7 Triple Kill Word of Misery", ItemId = 1148,
            InventoryIndex = 10, WearLocation = 2, PacketId = 0x099A,
            VariantKey = "weapon-with-three-cards", GroupNumber = 6,
            X = 818, Y = 919, IconSize = 58, IconPadding = 5,
            TextSize = 18, OpacityPercent = 88
        });
        settings.MvpTimers.Add(new MvpTimer
        {
            Name = "Valkyrie Randgris", MonsterId = 1751, DurationMinutes = 75,
            KilledAtUtc = new DateTime(2026, 10, 26, 7, 15, 0, DateTimeKind.Utc),
            CompletionSoundEnabled = true, CompletionSoundPath = "mvp.wav",
            CompletionSoundVolumePercent = 64
        });
        settings.RewardChannels.Clear();
        settings.RewardChannels.Add(new RewardChannelSettings
        {
            Kind = RewardKind.Loot, Enabled = true, GroupNumber = 7, Order = 2,
            TextColor = "#FF102030", EquipColor = "#FF203040",
            CardColor = "#FF304050", LootColor = "#FF405060",
            ConsumableColor = "#FF506070"
        });
        settings.RewardGroups.Clear();
        settings.RewardGroups.Add(new RewardGroupSettings
        {
            GroupNumber = 7, X = 120, Y = 230, TextSize = 27,
            DurationSeconds = 4.5, AggregationWindowMs = 975, MaxLines = 12, Gap = 8
        });
        settings.RateIndicators.Clear();
        settings.RateIndicators.Add(new RateIndicatorSettings
        {
            Kind = RateIndicatorKind.ZenyTotal, Enabled = true, GroupNumber = 8,
            X = 340, Y = 450, TextSize = 29, TextColor = "#FF708090", OpacityPercent = 67
        });
        settings.GoldGoblinPriceRules.Add(new GoldGoblinPriceRule
        {
            ItemId = 7539, Name = "Poring Coin", UseVendingPrice = true, ManualPrice = 12500
        });

        store.Save(settings);
        var restored = new SettingsStore(_root).Load();

        Assert.True(restored.OverlayCoordinatesRelativeToGame);
        Assert.Equal(GameServerProfile.SpringRo, restored.ServerProfile);
        Assert.Equal("en", restored.Language);
        Assert.Equal(("Home", "End", "Escape", "PageUp", "F6"),
            (restored.SettingsHotkey, restored.OverlaysHotkey, restored.FinishMoveHotkey,
                restored.TimersHotkey, restored.DatabaseHotkey));
        Assert.Equal((321.5, 654.25, 88d, 13d),
            (restored.NotificationX, restored.NotificationY, restored.NotificationSize, restored.NotificationGap));

        var skill = Assert.Single(restored.Skills);
        Assert.Equal((62, 3, 115d, 226d, 61d, 2d, 91d, 37d),
            (skill.PacketSkillId, skill.GroupNumber, skill.X, skill.Y, skill.IndividualIconSize,
                skill.IconPadding, skill.ReadyOpacityPercent, skill.CooldownOpacityPercent));
        Assert.Equal("BB", skill.ShortNameOverride);
        Assert.False(skill.ShowCountdown);

        var cooldownGroup = Assert.Single(restored.CooldownGroups);
        Assert.Equal((3, "Damage", "Vertical", 11d),
            (cooldownGroup.GroupNumber, cooldownGroup.Name, cooldownGroup.Direction, cooldownGroup.Gap));

        var effect = Assert.Single(restored.Effects);
        Assert.Equal((110, 4, 410d, 125d, 52d, 3d, 19d, 83d),
            (effect.StatusId, effect.GroupNumber, effect.NotificationX, effect.NotificationY,
                effect.NotificationIconSize, effect.NotificationIconPadding,
                effect.NotificationTextSize, effect.NotificationOpacityPercent));
        Assert.Equal(("appear.wav", 72d, "end.wav", 43d),
            (effect.AppearedSoundPath, effect.AppearedSoundVolumePercent,
                effect.DisappearedSoundPath, effect.DisappearedSoundVolumePercent));
        Assert.False(effect.ShowDurationText);
        Assert.True(effect.ShowDurationFill);

        var buffGroup = Assert.Single(restored.BuffPanelGroups);
        Assert.Equal((4, 444d, 555d, 47d, 1d, 7d, 96d, 28d),
            (buffGroup.GroupNumber, buffGroup.X, buffGroup.Y, buffGroup.IconSize,
                buffGroup.IconPadding, buffGroup.Gap, buffGroup.ActiveOpacityPercent,
                buffGroup.InactiveOpacityPercent));

        var reminder = Assert.Single(restored.Reminders);
        Assert.Equal((203, 5, 606d, 707d, 69d, 4d, 17d, 76d),
            (reminder.StatusId, reminder.GroupNumber, reminder.X, reminder.Y,
                reminder.IconSize, reminder.IconPadding, reminder.TextSize, reminder.OpacityPercent));
        Assert.False(reminder.ShowIcon);

        var equipment = Assert.Single(restored.Equipment);
        Assert.Equal((1148, 10, 2, 0x099A, 6),
            (equipment.ItemId, equipment.InventoryIndex, equipment.WearLocation,
                equipment.PacketId, equipment.GroupNumber));
        Assert.Equal("weapon-with-three-cards", equipment.VariantKey);

        var timer = Assert.Single(restored.MvpTimers);
        Assert.Equal((1751, 75d, "mvp.wav", 64d),
            (timer.MonsterId, timer.DurationMinutes, timer.CompletionSoundPath,
                timer.CompletionSoundVolumePercent));
        Assert.Equal(new DateTime(2026, 10, 26, 7, 15, 0, DateTimeKind.Utc), timer.KilledAtUtc);

        var reward = Assert.Single(restored.RewardChannels.Where(x => x.Kind == RewardKind.Loot));
        Assert.Equal((7, 2, "#FF102030", "#FF203040", "#FF304050", "#FF405060", "#FF506070"),
            (reward.GroupNumber, reward.Order, reward.TextColor, reward.EquipColor,
                reward.CardColor, reward.LootColor, reward.ConsumableColor));
        var rewardGroup = Assert.Single(restored.RewardGroups.Where(x => x.GroupNumber == 7));
        Assert.Equal((120d, 230d, 27d, 4.5, 975, 12, 8d),
            (rewardGroup.X, rewardGroup.Y, rewardGroup.TextSize, rewardGroup.DurationSeconds,
                rewardGroup.AggregationWindowMs, rewardGroup.MaxLines, rewardGroup.Gap));

        var rate = Assert.Single(restored.RateIndicators.Where(x => x.Kind == RateIndicatorKind.ZenyTotal));
        Assert.Equal((8, 340d, 450d, 29d, "#FF708090", 67d),
            (rate.GroupNumber, rate.X, rate.Y, rate.TextSize, rate.TextColor, rate.OpacityPercent));
        var priceRule = Assert.Single(restored.GoldGoblinPriceRules);
        Assert.Equal((7539, "Poring Coin", true, 12500L),
            (priceRule.ItemId, priceRule.Name, priceRule.UseVendingPrice, priceRule.ManualPrice));
    }

    [Fact]
    public void LegacyMigrationIsIdempotentAfterSaving()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "settings.json"), """
        {
          "SettingsVersion": 1,
          "IconSize": 80,
          "IconOpacity": 0.75,
          "Skills": [{ "Name": "Legacy skill", "GroupNumber": 9 }],
          "Effects": [{
            "Name": "Legacy effect", "SoundEnabled": true, "SoundPath": "legacy.wav",
            "NotificationX": 111, "NotificationY": 222, "NotificationSize": 66
          }]
        }
        """);
        var store = new SettingsStore(_root);

        var migrated = store.Load();
        store.Save(migrated);
        var loadedAgain = new SettingsStore(_root).Load();

        Assert.Equal(11, loadedAgain.SettingsVersion);
        var skill = Assert.Single(loadedAgain.Skills);
        Assert.Equal((1, 80d, 75d, 51d),
            (skill.GroupNumber, skill.IndividualIconSize,
                skill.ReadyOpacityPercent, skill.CooldownOpacityPercent));
        var effect = Assert.Single(loadedAgain.Effects);
        Assert.Equal((111d, 222d, 66d, "legacy.wav", "legacy.wav"),
            (effect.NotificationX, effect.NotificationY, effect.NotificationIconSize,
                effect.AppearedSoundPath, effect.DisappearedSoundPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
