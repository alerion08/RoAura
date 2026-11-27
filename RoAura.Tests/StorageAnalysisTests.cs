using System.Text.Json;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class StorageAnalysisTests
{
    [Fact]
    public void StorageGroupsPreserveModeSlotAndQuantity()
    {
        using var storage = JsonDocument.Parse("""
            {"sc":[{"nameid":501,"amount":6}], "sc3":[{"nameid":"501","amount":"3"}],
             "hc2":[{"nameid":501,"amount":10}], "hc3":null}
            """);
        using var meta = JsonDocument.Parse("""
            {"501":{"name":"Red Potion","type":"Healing"}}
            """);
        var entries = StorageAnalysis.Parse(storage.RootElement, meta.RootElement);
        Assert.Equal(new[] { "SC", "SC", "HC" }, entries.Select(entry => entry.Mode));
        Assert.Equal(new[] { 1, 3, 2 }, entries.Select(entry => entry.Slot));
        Assert.Equal(9L, entries.Where(entry => entry.Mode == "SC").Sum(entry => entry.Amount));
        Assert.All(entries, entry => Assert.Equal("Red Potion", entry.Name));
    }

    [Fact]
    public void ReadinessCapsSurplusAndShowsDeficits()
    {
        var craft = new StorageAnalysis.Craft("Fixture", 1,
            [new(1, "A", 10, 100), new(2, "B", 10, 2)]);
        Assert.Equal(60, craft.Percent);
        Assert.Equal(0L, craft.Repeats);
        var ready = craft with { Materials = [new(1, "A", 10, 100), new(2, "B", 10, 23)] };
        Assert.Equal(2L, ready.Repeats);
        Assert.Equal(0L, craft.Materials[0].Missing);
        Assert.Equal(8L, craft.Materials[1].Missing);
    }
}
