using System.Text.Json;
using System.Windows;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class OverlayLayoutEditorTests
{
    private static AppSettings Settings() => new() { ServerProfile = "SpringRO" };

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    public void One_grip_moves_the_whole_group_in_client_dips_including_disabled_members(double dpi)
    {
        var settings = Settings();
        var first = new SkillAura { GroupNumber = 7, X = 60, Y = 100, IndividualIconSize = 40 };
        var second = new SkillAura { GroupNumber = 7, X = 140, Y = 100, IndividualIconSize = 40, Enabled = false };
        var other = new SkillAura { GroupNumber = 8, X = 240, Y = 100 };
        settings.Skills.Add(first); settings.Skills.Add(second); settings.Skills.Add(other);
        var targets = OverlayLayoutTargets.Build(settings);
        var target = Assert.Single(targets, target => target.Key == "cooldown:7");
        Assert.Equal(2, target.Items.Count);
        Assert.Equal(60, first.X);
        var drag = target.BeginDrag(new Size(800, 600));
        drag.MoveScreen(new Point(100, 100), new Point(100 + 40 * dpi, 100 + 30 * dpi), dpi);
        Assert.Equal(100, first.X, 5); Assert.Equal(180, second.X, 5);
        Assert.Equal(130, first.Y, 5); Assert.Equal(130, second.Y, 5);
        Assert.Equal(240, other.X);
        drag.Move(new Vector(5000, 5000));
        Assert.Equal(800, second.X + second.IndividualIconSize, 5);
        Assert.Equal(600, second.Y + second.IndividualIconSize, 5);
        Assert.Equal(80, second.X - first.X, 5);
        drag.Move(new Vector(-5000, -5000));
        Assert.Equal(0, first.X, 5); Assert.Equal(0, first.Y, 5);
        Assert.Equal(80, second.X - first.X, 5);
    }

    [Fact]
    public void Catalog_preserves_outside_positions_and_keeps_character_scopes_and_packet_gate()
    {
        var settings = Settings();
        var first = new SkillAura { GroupNumber = 3, X = 5000, Y = -50 };
        var foreign = new SkillAura { GroupNumber = 3, X = 123, CharacterScope = new() { IsAllowed = false } };
        settings.Skills.Add(first); settings.Skills.Add(foreign);
        var target = OverlayLayoutTargets.Build(settings).Single(target => target.Key == "cooldown:3");
        Assert.Single(target.Items);
        _ = target.BeginDrag(new Size(800, 600));
        Assert.Equal(5000, first.X); Assert.Equal(-50, first.Y);
        target.BeginDrag(new Size(800, 600)).Move(new Vector(-10, 10));
        Assert.InRange(first.X, 0, 800 - first.IndividualIconSize);
        Assert.Equal(0, first.Y); Assert.Equal(123, foreign.X);
        settings.PacketCaptureEnabled = false;
        var keys = OverlayLayoutTargets.Build(settings).Select(target => target.Key).ToArray();
        Assert.DoesNotContain(keys, key => key.StartsWith("cooldown:") || key.StartsWith("statistics:") || key.StartsWith("reward:"));
        Assert.Contains("mvp", keys); Assert.Contains("quest", keys);
        settings.ServerProfile = "Other";
        var otherTargets = OverlayLayoutTargets.Build(settings);
        Assert.Contains(otherTargets, target => target.Key == "mvp");
        Assert.DoesNotContain(otherTargets, target => target.Key == "market" || target.Key.StartsWith("statistics:"));
        settings.PacketCaptureEnabled = true;
        Assert.Contains(OverlayLayoutTargets.Build(settings), target => target.Key.StartsWith("statistics:"));
    }

    [Fact]
    public void All_overlay_families_have_group_or_section_targets_and_ungrouped_rows_are_not_individual_targets()
    {
        var settings = Settings();
        settings.Skills.Add(new() { GroupNumber = 0 }); settings.Skills.Add(new() { GroupNumber = 0, X = 180 });
        settings.Effects.Add(new() { GroupNumber = 2, ShowOnBuffPanel = true });
        settings.Reminders.Add(new() { GroupNumber = 3 }); settings.Equipment.Add(new() { GroupNumber = 4 });
        settings.SyncBuffPanelGroups(); settings.SyncRewardGroups(); settings.SyncRateIndicators();
        settings.TimersOverlayVisible = false;
        var targets = OverlayLayoutTargets.Build(settings);
        Assert.Equal(2, targets.Single(target => target.Key == "cooldown:0").Items.Count);
        foreach (var key in new[] { "effect:2", "buff:2", "reminder:3", "equipment:4", "statistics:1", "reward:1", "mvp", "market", "quest" })
            Assert.Contains(targets, target => target.Key == key);
        var originalX = settings.QuestOverlay.X;
        targets.Single(target => target.Key == "quest").BeginDrag(new Size(2000, 2000)).Move(new Vector(10, 20));
        Assert.Equal(originalX + 10, settings.QuestOverlay.X);
        Assert.False(settings.TimersOverlayVisible);
        var statistic = targets.Single(target => target.Key == "statistics:1");
        Assert.Equal(settings.RateIndicators.Count, statistic.Items.Count);
    }

    [Fact]
    public void Runtime_previews_do_not_change_cooldown_or_persist_in_presets_and_layout_hotkeys_do_not_replace_existing_keys()
    {
        var skill = new SkillAura { ReadyOpacityPercent = 0, IsReady = true, RemainingSeconds = 0, IsPlacementPreview = true };
        Assert.True(skill.CurrentOpacity > 0); Assert.True(skill.IsReady); Assert.Equal(0, skill.RemainingSeconds);
        var restored = JsonSerializer.Deserialize<SkillAura>(JsonSerializer.Serialize(skill))!;
        Assert.False(restored.IsPlacementPreview); Assert.Equal(0, restored.CurrentOpacity);
        var settings = Settings();
        Assert.Equal("None", settings.InterfaceLayoutHotkey); Assert.False(OverlayLayoutHotkeys.Enabled(settings));
        settings.InterfaceLayoutHotkey = "Ctrl+Shift+F2";
        Assert.True(OverlayLayoutHotkeys.Enabled(settings)); Assert.False(settings.HotkeyConflicts);
        settings.InterfaceLayoutHotkey = settings.StartSessionHotkey = "Ctrl+F3";
        Assert.False(OverlayLayoutHotkeys.Enabled(settings)); Assert.True(settings.HotkeyConflicts);
        Assert.True(FarmingHotkeys.Enabled(settings, settings.StartSessionHotkey));
        settings.InterfaceLayoutHotkey = "None";
        Assert.False(settings.HotkeyConflicts); Assert.True(FarmingHotkeys.Enabled(settings, settings.StartSessionHotkey));
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("None", copy.InterfaceLayoutHotkey);
    }
}
