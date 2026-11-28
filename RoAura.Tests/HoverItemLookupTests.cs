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
    public void LocatesDarkTooltipAndIgnoresUnreadablePunctuation()
    {
        const int width = 140, height = 60;
        var pixels = Enumerable.Repeat((byte)225, width * height).ToArray();
        for (var y = 13; y <= 28; y++)
        for (var x = 18; x < 115; x++)
            pixels[y * width + x] = (byte)(y is > 16 and < 25 && x % 5 == 0 ? 235 : 70);

        var bounds = HoverItemLookup.FindTooltip(pixels, width, height, 28, 33);
        Assert.Equal(new System.Windows.Int32Rect(18, 13, 97, 16), bounds);
        Assert.Equal("", HoverItemLookup.SearchText("@@@..."));
        DatabaseItem[] items = [new() { Id = 1, Name = "Tiger's Footskin" }];
        Assert.Equal(1, Assert.Single(HoverItemLookup.Suggest("Tlger’s.Footskln", items)).Id);
    }

    [Fact]
    public void RecognizedTextSurvivesWhenOnlyApproximateMatchesExist()
    {
        DatabaseItem[] catalog = [new() { Id = 1, Name = "White Herb" }, new() { Id = 2, Name = "Red Herb" }];
        Assert.Empty(HoverItemLookup.Match("Whlte Herb: 1 ea.", catalog));
        Assert.Equal("Whlte Herb", HoverItemLookup.SearchText("Whlte Herb: 1 ea."));
        Assert.Equal(1, Assert.Single(HoverItemLookup.Suggest("Whlte Herb: 1 ea.", catalog)).Id);
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
