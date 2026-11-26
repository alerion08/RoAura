using RoAura.Models;
using Xunit;

namespace RoAura.Tests;

public sealed class EquipmentTrackerTests
{
    [Fact]
    public void Unlearned_item_reports_no_data()
    {
        var item = new EquipmentTracker();

        Assert.Equal("NoEquipmentData", item.CurrentStateText);
        Assert.False(item.IsVisible);
    }

    [Fact]
    public void Learned_item_reports_equipped_state_without_losing_fingerprint()
    {
        var item = new EquipmentTracker
        {
            ItemId = 1148,
            InventoryIndex = 17,
            WearLocation = 2,
            PacketId = 0x099A,
            VariantKey = "1148|+7|cards:1001,1001,1001|grade:0|options:none",
            DetectedItemName = "+7 Triple Kill Word of Misery",
            IsEquipped = true
        };

        Assert.Equal("Equipped", item.CurrentStateText);
        Assert.True(item.IsVisible);
        Assert.Contains("cards:1001,1001,1001", item.VariantKey);

        item.IsEquipped = false;

        Assert.Equal("NotEquipped", item.CurrentStateText);
        Assert.False(item.IsVisible);
        Assert.Equal("1148|+7|cards:1001,1001,1001|grade:0|options:none", item.VariantKey);
    }

    [Fact]
    public void Database_icon_follows_item_id_and_preserves_the_manual_fallback()
    {
        const string manual = "bWFudWFs";
        const string catalog = "Y2F0YWxvZw==";
        var item = new EquipmentTracker { ItemId = 1148, IconImageBase64 = manual };
        item.SetDatabaseIconResolver(id => id == 1148 ? catalog : null);

        Assert.Equal(manual, item.EffectiveIconImageBase64);
        item.UseDatabaseIcon = true;
        Assert.Equal(catalog, item.EffectiveIconImageBase64);
        item.ItemId = 1149;
        Assert.Equal(manual, item.EffectiveIconImageBase64);
        item.ItemId = 1148;
        Assert.Equal(catalog, item.EffectiveIconImageBase64);
        item.SetDatabaseIconResolver(_ => null);
        Assert.Equal(manual, item.EffectiveIconImageBase64);
        Assert.Equal(manual, item.IconImageBase64);
    }

    [Fact]
    public void Text_overlay_position_and_opacity_survive_settings_round_trip()
    {
        var item = System.Text.Json.JsonSerializer.Deserialize<EquipmentTracker>("{}")!;
        Assert.True(item.ShowTextBelowIcon);
        Assert.False(item.ShowTextOverIcon);

        item.ShowTextOnIcon = true;
        item.TextBackgroundOpacityPercent = 37;
        var restored = System.Text.Json.JsonSerializer.Deserialize<EquipmentTracker>(
            System.Text.Json.JsonSerializer.Serialize(item))!;
        Assert.True(restored.ShowTextOverIcon);
        Assert.False(restored.ShowTextBelowIcon);
        Assert.Equal(37d, restored.TextBackgroundOpacityPercent);

        restored.ShowText = false;
        Assert.False(restored.ShowTextOverIcon);
        Assert.False(restored.ShowTextBelowIcon);
        restored.TextBackgroundOpacityPercent = 120;
        Assert.Equal(100d, restored.TextBackgroundOpacityPercent);
    }

    [Fact]
    public void Placement_preview_does_not_change_equipped_state()
    {
        var item = new EquipmentTracker { ItemId = 1148, IsEquipped = false };

        item.IsPlacementPreview = true;

        Assert.True(item.IsVisible);
        Assert.False(item.IsEquipped);
        Assert.Equal("NotEquipped", item.CurrentStateText);
    }
}
