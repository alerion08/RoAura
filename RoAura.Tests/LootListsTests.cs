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
