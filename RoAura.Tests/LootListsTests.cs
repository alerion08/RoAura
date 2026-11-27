using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class LootListsTests
{
    [Fact]
    public void PickupsAdvanceOnlyActiveRowsOnTheMatchingServer()
    {
        var first = new LootList { Server = GameServerProfile.RagnarokPlus };
        var second = new LootList { Server = GameServerProfile.RagnarokPlus };
        var other = new LootList { Server = GameServerProfile.SpringRo };
        var settings = new AppSettings { LootLists = [first, second, other] };
        foreach (var list in settings.LootLists)
            Assert.True(LootLists.AddItem(list, 501, "Red Potion"));
        first.Items[0].Target = 25;
        second.Items[0].Active = false;

        Assert.True(LootLists.RecordPickup(settings, "RagnarokPlus", 501, 5));
        Assert.Equal(5L, first.Items[0].Acquired);
        Assert.Equal(20L, first.Items[0].Remaining);
        Assert.Equal(0L, second.Items[0].Acquired);
        Assert.Equal(0L, other.Items[0].Acquired);
        Assert.False(LootLists.RecordPickup(settings, GameServerProfile.RagnarokPlus, 501, -1));
    }

    [Fact]
    public void InactiveLootPausesStatisticsUntilAnyMatchingRowIsActive()
    {
        var server = GameServerProfile.RagnarokPlus;
        var item = new LootListItem { ItemId = 501, Active = false };
        var other = new LootListItem { ItemId = 501 };
        var settings = new AppSettings
        {
            LootLists =
            [
                new LootList { Server = server, Items = [item] },
                new LootList { Server = GameServerProfile.SpringRo, Items = [other] }
            ]
        };

        Assert.False(LootLists.IsPickupPaused(settings, server, 502));
        Assert.True(LootLists.IsPickupPaused(settings, server, 501));
        Assert.False(LootLists.RecordPickup(settings, server, 501, 3));
        item.Active = true;
        Assert.False(LootLists.IsPickupPaused(settings, server, 501));
        Assert.True(LootLists.RecordPickup(settings, server, 501, 2));
        Assert.Equal(2, item.Acquired);
        item.Active = false;
        settings.LootLists.Add(new LootList { Server = server, Items = [new LootListItem { ItemId = 501 }] });
        Assert.False(LootLists.IsPickupPaused(settings, server, 501));
    }

    [Fact]
    public void TrackerVisibilityIsPerItemAndCompletionUpdatesWithProgress()
    {
        var list = new LootList { ShowInTracker = true };
        var hidden = new LootListItem { ShowInTracker = false };
        var visible = new LootListItem { Target = 5 };
        list.Items = [hidden, visible];
        Assert.Same(visible, Assert.Single(LootLists.TrackerItems(list)));

        var completionUpdates = 0;
        visible.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LootListItem.IsComplete)) completionUpdates++;
        };
        visible.Acquired = 6;
        Assert.True(visible.IsComplete);
        visible.Target = 7;
        Assert.False(visible.IsComplete);
        Assert.Equal(2, completionUpdates);
    }

    [Fact]
    public void ChangingListsPreservesProgressAndRejectsDuplicateMembership()
    {
        var first = new LootList { Server = GameServerProfile.RagnarokPlus };
        var second = new LootList { Server = GameServerProfile.RagnarokPlus };
        var other = new LootList { Server = GameServerProfile.SpringRo };
        LootLists.AddItem(first, 501, "Red Potion");
        var row = first.Items[0];
        row.Acquired = 5;
        row.Target = 25;
        Assert.False(LootLists.MoveItem(first, other, row));
        Assert.True(LootLists.MoveItem(first, second, row));
        Assert.Same(row, Assert.Single(second.Items));
        Assert.Equal(20L, row.Remaining);
        Assert.False(LootLists.AddItem(second, 501, "Red Potion"));
        Assert.Empty(first.Items);
    }

    [Fact]
    public void QuestImportCreatesOneListAndPreservesProgressAfterRename()
    {
        var settings = new AppSettings();
        var server = GameServerProfile.RagnarokPlus;
        Assert.Equal(1, LootLists.ImportQuest(settings, server, "quest:id", "Quest", [(501, "Red Potion", 5L)]));
        var list = Assert.Single(settings.LootLists);
        Assert.True(list.ShowInTracker);
        list.ShowInTracker = false;
        list.Name = "Renamed";
        list.Items[0].Acquired = 3;
        Assert.Equal(1, LootLists.ImportQuest(settings, server, "quest:id", "Quest",
            [(501, "Red Potion", 9L), (502, "Orange Potion", 2L)]));
        Assert.Same(list, Assert.Single(settings.LootLists));
        Assert.True(list.ShowInTracker);
        Assert.Equal("Renamed", list.Name);
        Assert.Equal(3L, list.Items[0].Acquired);
        Assert.Equal(5L, list.Items[0].Target);
    }
}
