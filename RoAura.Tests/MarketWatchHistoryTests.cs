using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class MarketWatchHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoAura.Tests", Guid.NewGuid().ToString("N"));
    private string HistoryPath => Path.Combine(_root, MarketWatchHistoryStore.FileName);
    private static MarketWatchRule Rule => new() { ItemId = 1251, ItemName = "Jur [3]", MaximumPrice = 200_000 };
    private static MarketListing Offer(string owner = "Alice", string price = "150,000 Zeny") => new()
    {
        ItemId = 1251, ItemName = "Jur", ShopType = "SELL", Stall = "Shop · Магазин", Owner = owner,
        Map = "prontera", X = 123, Y = 87, Price = price, Quantity = "4", Refine = "+7", Cards = "Hydra"
    };

    [Fact]
    public void RestartSuppressesAllKnownShopsButFindsNewSellerOrPrice()
    {
        var first = new MarketWatchService(new MarketWatchHistoryStore(HistoryPath));
        Assert.Single(first.Evaluate([Rule], [Offer(), Offer("Bob")]));
        Assert.Equal(2, first.History.Count);
        Assert.True(File.Exists(HistoryPath));
        var restarted = new MarketWatchService(new MarketWatchHistoryStore(HistoryPath));
        Assert.Empty(restarted.Evaluate([Rule], [Offer(), Offer("Bob")]));
        Assert.Empty(restarted.Evaluate([Rule], []));
        Assert.Empty(restarted.Evaluate([Rule], [Offer()]));
        Assert.Single(restarted.Evaluate([Rule], [Offer("Claire")]));
        Assert.Single(restarted.Evaluate([Rule], [Offer(price: "140000")]));
        Assert.Equal(4, restarted.History.Count);
        Assert.False(restarted.HistoryPersistenceFailed);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void HistoryRecordsEveryQualifyingShopWithDetectionTimeCoordinatesAndPrice()
    {
        var now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        var service = new MarketWatchService(new MarketWatchHistoryStore(HistoryPath), () => now);
        var buying = Offer("Buyer"); buying.ShopType = "BUY";
        Assert.Single(service.Evaluate([Rule], [Offer(), Offer("Bob"), Offer(), buying, Offer("High", "300000")]));
        Assert.Equal(2, service.History.Count);
        var entry = service.History[0];
        Assert.Equal(now, entry.DetectedAtUtc);
        Assert.Equal("Jur [3]", entry.ItemName);
        Assert.Equal("Shop · Магазин", entry.ShopCaption);
        Assert.Equal("prontera (123,87)", entry.Coordinates);
        Assert.Equal(150_000, entry.Price);
        var quantityChange = Offer(); quantityChange.Quantity = "1";
        Assert.Empty(service.Evaluate([Rule], [quantityChange]));
        Assert.Equal(2, service.History.Count);
    }

    [Fact]
    public void RetentionPrunesOnStartupOrTimerAndExpiredOffersCanNotifyAgain()
    {
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = new MarketWatchService(new MarketWatchHistoryStore(HistoryPath), () => now);
        service.Evaluate([Rule], [Offer()], retentionDays: 2);
        now = now.AddDays(1);
        Assert.Empty(service.Evaluate([Rule], [Offer()], retentionDays: 2));
        now = now.AddDays(1);
        service.PruneHistory(2);
        Assert.Empty(service.History);
        var restarted = new MarketWatchService(new MarketWatchHistoryStore(HistoryPath), () => now);
        Assert.Empty(restarted.History);
        Assert.Single(restarted.Evaluate([Rule], [Offer()], retentionDays: 2));
        now = now.AddDays(31);
        var afterDowntime = new MarketWatchService(new MarketWatchHistoryStore(HistoryPath), () => now);
        afterDowntime.PruneHistory(2);
        Assert.Empty(afterDowntime.History);
    }

    [Fact]
    public void BrokenOrNewerHistoryIsPreservedAndPersistenceFailureIsVisible()
    {
        Directory.CreateDirectory(_root);
        foreach (var content in new[] { "broken json", "{\"Version\":2,\"Entries\":[]}" })
        {
            File.WriteAllText(HistoryPath, content);
            var service = new MarketWatchService(new MarketWatchHistoryStore(HistoryPath));
            Assert.True(service.HistoryPersistenceFailed);
            Assert.Single(service.Evaluate([Rule], [Offer()]));
            Assert.Equal(content, File.ReadAllText(HistoryPath));
            Assert.Empty(service.Evaluate([Rule], [Offer()]));
        }
    }

    [Fact]
    public void WriteFailureKeepsInMemoryDeduplicationAndCanRetryAfterStorageRecovers()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "blocked");
        File.WriteAllText(blocker, "not a directory");
        var store = new MarketWatchHistoryStore(Path.Combine(blocker, MarketWatchHistoryStore.FileName));
        var service = new MarketWatchService(store);
        Assert.Single(service.Evaluate([Rule], [Offer()]));
        Assert.True(service.HistoryPersistenceFailed);
        Assert.Empty(service.Evaluate([Rule], [Offer()]));
        File.Delete(blocker);
        Assert.Empty(service.Evaluate([Rule], [Offer()]));
        Assert.False(service.HistoryPersistenceFailed);
        var restarted = new MarketWatchService(new MarketWatchHistoryStore(store.FilePath));
        Assert.Empty(restarted.Evaluate([Rule], [Offer()]));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
