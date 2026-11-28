using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class HoverItemLookupTests
{
    [Theory]
    [InlineData("White Herb: 1 ea.", 1)]
    [InlineData("Fiasco's White Potion", 3)]
    [InlineData("+7 Fiasco's White Potion", 3)]
    [InlineData("+10 Fiasco's White Herb", 1)]
    [InlineData("Old Blue Box", 4)]
    public void PrefersExactNamesBeforeRemovingModifiers(string tooltip, int expected)
    {
        DatabaseItem[] catalog =
        [
            new() { Id = 1, Name = "White Herb" },
            new() { Id = 2, Name = "White Potion" },
            new() { Id = 3, Name = "Fiasco's White Potion" },
            new() { Id = 4, Name = "Old Blue Box" },
            new() { Id = 5, Name = "Blue Box" }
        ];
        Assert.Equal(expected, Assert.Single(HoverItemLookup.Match(tooltip, catalog)).Id);
    }

    [Fact]
    public void ReturnsDistinctChoicesForIdenticalNames()
    {
        DatabaseItem[] catalog =
        [
            new() { Id = 1, Name = "Sword" },
            new() { Id = 2, Name = "Sword" },
            new() { Id = 1, Name = "Sword" }
        ];
        Assert.Equal(new[] { 1, 2 }, HoverItemLookup.Match("+7 Sword", catalog).Select(item => item.Id));
    }
}
