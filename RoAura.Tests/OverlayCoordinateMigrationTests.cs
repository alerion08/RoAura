using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class OverlayCoordinateMigrationTests
{
    [Fact]
    public void ConvertsEveryPersistedOverlayCoordinateExactlyOnce()
    {
        var settings = new AppSettings
        {
            OverlayCoordinatesRelativeToGame = false,
            NotificationX = 310,
            NotificationY = 420,
            TimersX = 510,
            TimersY = 620
        };
        settings.Skills.Add(new SkillAura { X = 110, Y = 220 });
        settings.Effects.Add(new TrackedEffect { NotificationX = 210, NotificationY = 320 });
        settings.Reminders.Add(new ReminderItem { X = 410, Y = 520 });
        settings.BuffPanelGroups.Add(new BuffPanelGroup { X = 610, Y = 720 });
        settings.Equipment.Add(new EquipmentTracker { X = 710, Y = 820 });
        settings.RewardGroups.Clear();
        settings.RewardGroups.Add(new RewardGroupSettings { X = 810, Y = 920 });
        settings.RateIndicators.Clear();
        settings.RateIndicators.Add(new RateIndicatorSettings { X = 910, Y = 1020 });

        Assert.True(OverlayCoordinateMigration.Apply(settings, 100, 200));

        Assert.True(settings.OverlayCoordinatesRelativeToGame);
        Assert.Equal((210d, 220d), (settings.NotificationX, settings.NotificationY));
        Assert.Equal((10d, 20d), (settings.Skills[0].X, settings.Skills[0].Y));
        Assert.Equal((110d, 120d),
            (settings.Effects[0].NotificationX, settings.Effects[0].NotificationY));
        Assert.Equal((310d, 320d), (settings.Reminders[0].X, settings.Reminders[0].Y));
        Assert.Equal((510d, 520d), (settings.BuffPanelGroups[0].X, settings.BuffPanelGroups[0].Y));
        Assert.Equal((610d, 620d), (settings.Equipment[0].X, settings.Equipment[0].Y));
        Assert.Equal((410d, 420d), (settings.TimersX, settings.TimersY));
        Assert.Equal((710d, 720d), (settings.RewardGroups[0].X, settings.RewardGroups[0].Y));
        Assert.Equal((810d, 820d), (settings.RateIndicators[0].X, settings.RateIndicators[0].Y));

        Assert.False(OverlayCoordinateMigration.Apply(settings, 100, 200));
        Assert.Equal((10d, 20d), (settings.Skills[0].X, settings.Skills[0].Y));
        Assert.Equal((410d, 420d), (settings.TimersX, settings.TimersY));
        Assert.Equal((810d, 820d), (settings.RateIndicators[0].X, settings.RateIndicators[0].Y));
    }
}
