using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using RoAura.Controls;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class OverlayLayoutPreservationTests
{
    [Fact]
    public void HostResize_ClientSwitchAndDpiChange_DoNotOverwriteAnySavedPosition()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "RoAura.Tests", Guid.NewGuid().ToString("N"));
            var store = new SettingsStore(root);
            var settings = store.Load();
            settings.OverlayCoordinatesRelativeToGame = true;
            settings.Skills.Add(new SkillAura { X = 1800, Y = 900 });
            settings.Effects.Add(new TrackedEffect { NotificationX = 1700, NotificationY = 800 });
            settings.Reminders.Add(new ReminderItem { X = 1600, Y = 700 });
            settings.BuffPanelGroups.Add(new BuffPanelGroup { X = 1500, Y = 600 });
            settings.Equipment.Add(new EquipmentTracker { X = 1400, Y = 500 });
            settings.SyncBuffPanelGroups();
            settings.SyncRewardGroups();
            settings.SyncRateIndicators();
            var original = Coordinates(settings);
            store.Save(settings);
            var stored = File.ReadAllText(store.SettingsPath);
            var overlay = new OverlayWindow(settings, store,
                new GoldGoblinService(settings, new SpringRoMarketService()));
            try
            {
                Assert.Equal(original, Coordinates(settings));
                var apply = typeof(OverlayWindow).GetMethod("ApplyGameWindowBounds",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                void Resize(int handle, int width, int height, double scale)
                {
                    apply.Invoke(overlay, new object[] { new IntPtr(handle),
                        new ForegroundProcess.WindowBounds(0, 0, width, height),
                        new Matrix(scale, 0, 0, scale, 0, 0), true });
                    Assert.Equal(original, Coordinates(settings));
                    Assert.Equal(stored, File.ReadAllText(store.SettingsPath));
                    var timer = (DispatcherTimer)typeof(OverlayWindow).GetField("_saveTimer",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
                    Assert.False(timer.IsEnabled);
                }
                Resize(1, 2560, 1440, 1);
                Resize(1, 1, 1, 1); // Transient tiny client rectangle.
                Resize(1, 0, 0, 1); // Invalid/minimized bounds.
                Resize(2, 640, 480, 1); // Another client.
                Resize(2, 1920, 1080, 2d / 3d); // 150% DPI.
                Resize(1, 2560, 1440, 1);
                store.Save(settings);
                Assert.Equal(original, Coordinates(store.Load()));
            }
            finally
            {
                overlay.Close();
                Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void CoordinateEditors_InitializeAndSwitchSelection_WithoutWritingZero()
    {
        RunSta(() =>
        {
            var first = new ReminderItem { X = 1450, Y = 850 };
            var second = new ReminderItem { X = 1750, Y = 950 };
            var x = new NumericTextBox();
            var y = new NumericTextBox();
            x.SetBinding(NumericTextBox.ValueProperty, new Binding("X") { Mode = BindingMode.TwoWay });
            y.SetBinding(NumericTextBox.ValueProperty, new Binding("Y") { Mode = BindingMode.TwoWay });
            foreach (var model in new[] { first, second, first })
            {
                x.DataContext = model;
                y.DataContext = model;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.Equal(model.X, x.Value);
                Assert.Equal(model.Y, y.Value);
                x.CommitInput();
                y.CommitInput();
                Assert.Equal((1450d, 850d), (first.X, first.Y));
                Assert.Equal((1750d, 950d), (second.X, second.Y));
            }
        });
    }

    [Fact]
    public void MarketMovePreviewUsesTheSharedEditLifecycleAndNeverErasesCoordinates()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "RoAura.Tests", Guid.NewGuid().ToString("N"));
            var store = new SettingsStore(root);
            var settings = store.Load();
            settings.ServerProfile = GameServerProfile.SpringRo;
            settings.MarketWatchNotifications.X = 450;
            settings.MarketWatchNotifications.Y = 160;
            var overlay = new OverlayWindow(settings, store, new GoldGoblinService(settings, new SpringRoMarketService()));
            try
            {
                overlay.SetMarketWatchEditMode();
                Assert.True(overlay.IsBusyEditing);
                Assert.True(Assert.Single(settings.ActiveMarketNotifications).IsPlacementPreview);
                overlay.EndAllEditModes();
                Assert.False(overlay.IsBusyEditing);
                Assert.Empty(settings.ActiveMarketNotifications);
                Assert.Equal(450, settings.MarketWatchNotifications.X);
                Assert.Equal(160, settings.MarketWatchNotifications.Y);
                Assert.Equal(450, store.Load().MarketWatchNotifications.X);
            }
            finally { overlay.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }

    private static string Coordinates(AppSettings s) => JsonSerializer.Serialize(new
    {
        s.NotificationX, s.NotificationY, s.TimersX, s.TimersY,
        Market = new { s.MarketWatchNotifications.X, s.MarketWatchNotifications.Y },
        Skills = s.Skills.Select(v => new { v.X, v.Y }),
        Effects = s.Effects.Select(v => new { v.NotificationX, v.NotificationY }),
        Reminders = s.Reminders.Select(v => new { v.X, v.Y }),
        Buffs = s.BuffPanelGroups.Select(v => new { v.X, v.Y }),
        Equipment = s.Equipment.Select(v => new { v.X, v.Y }),
        Rewards = s.RewardGroups.Select(v => new { v.X, v.Y }),
        Rates = s.RateIndicators.Select(v => new { v.X, v.Y })
    });

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Layout preservation test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
