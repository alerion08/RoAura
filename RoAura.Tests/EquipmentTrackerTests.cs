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
    public void Placement_preview_does_not_change_equipped_state()
    {
        var item = new EquipmentTracker { ItemId = 1148, IsEquipped = false };

        item.IsPlacementPreview = true;

        Assert.True(item.IsVisible);
        Assert.False(item.IsEquipped);
        Assert.Equal("NotEquipped", item.CurrentStateText);
    }
}
