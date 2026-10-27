using System.Net;
using System.Net.Http;
using System.Text.Json;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class MarketWatchTests
{
    private static MarketWatchRule Rule(bool any = false, long maximum = 100) =>
        new() { ItemId = 730, ItemName = "Diamond", WithoutPrice = any, MaximumPrice = maximum };
    private static MarketListing Listing(string price = "100 z", string owner = "Alice", string type = "SELL") =>
        new() { ItemId = 730, ItemName = "Diamond", Price = price, Owner = owner, ShopType = type };

    [Fact]
    public void PausingKeepsTheRuleAndResumingOnlyFindsPreviouslyUnknownOffers()
    {
        var rule = Rule(maximum: 150);
        var service = new MarketWatchService();
        Assert.Single(service.Evaluate([rule], [Listing()]));
        rule.Enabled = false;
        Assert.Empty(service.Evaluate([rule], [Listing("50 z", "Bob")]));
        Assert.Equal(150, rule.MaximumPrice);
        Assert.Equal("Diamond", rule.ItemName);
        rule.Enabled = true;
        Assert.Empty(service.Evaluate([rule], [Listing()]));
        Assert.Single(service.Evaluate([rule], [Listing("50 z", "Bob")]));
    }

    [Fact]
    public void OlderSettingsDefaultToEnabledRulesAndVisibleIconsAtFullVolume()
    {
        Assert.True(JsonSerializer.Deserialize<MarketWatchRule>("{\"ItemId\":730}")!.Enabled);
        var config = JsonSerializer.Deserialize<MarketWatchNotificationSettings>("{}")!;
        Assert.True(config.ShowIcon);
        Assert.Equal(100, config.SoundVolumePercent);
    }

    [Theory]
    [InlineData(400, 100, 1000, 800, 400, 300, 700, 350)]
    [InlineData(-1500, 50, 800, 600, 400, 300, -1300, 200)]
    public void DialogCentersInTheGapIncludingNegativeMonitorCoordinates(double x, double y, double width, double height,
        double dialogWidth, double dialogHeight, double expectedX, double expectedY)
    {
        var result = MarketWatchDialog.CenterPosition(new System.Windows.Rect(x, y, width, height), new System.Windows.Size(dialogWidth, dialogHeight));
        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
    }

    [Fact]
    public void DatabaseSearchKeepsEquipmentSlotVariantsDistinct()
    {
        var database = new GameDatabaseService();
        Assert.Equal("Jur [2]", database.Item(1250)?.Name);
        Assert.Equal("Jur [3]", database.Item(1251)?.Name);
        var variants = database.Search(DatabaseSearchKind.Items, "Jur").ToList();
        Assert.Contains(variants, item => item.Caption == "Jur [2]");
        Assert.Contains(variants, item => item.Caption == "Jur [3]");
    }

    [Fact]
    public void PriceThresholdIncludesEqualityAndIgnoresBuyingOtherIdsAndInvalidPrices()
    {
        var service = new MarketWatchService();
        Assert.Empty(service.Evaluate([Rule()], [Listing("101 z"), Listing(type: "BUY"), Listing("?"),
            new MarketListing { ItemId = 731, Price = "1" }]));
        Assert.Equal(100, Assert.Single(service.Evaluate([Rule()], [Listing()])).Price);
    }

    [Fact]
    public void AnyPriceAndLargePriceValuesAreSupported()
    {
        var match = Assert.Single(new MarketWatchService().Evaluate([Rule(any: true)], [Listing("9,000,000,000 z")]));
        Assert.Equal(9_000_000_000, match.Price);
        Assert.Empty(new MarketWatchService().Evaluate([Rule(any: true)], [Listing("99999999999999999999999999")]));
    }

    [Fact]
    public void SameOfferIsSilentAndAbsenceDoesNotForgetItButNewSellerOrPriceNotifies()
    {
        var service = new MarketWatchService();
        Assert.Single(service.Evaluate([Rule()], [Listing()]));
        Assert.Empty(service.Evaluate([Rule()], [Listing()]));
        var changedQuantity = Listing(); changedQuantity.Quantity = "3";
        Assert.Empty(service.Evaluate([Rule()], [changedQuantity]));
        Assert.Single(service.Evaluate([Rule()], [Listing(), Listing(owner: "Bob")]));
        Assert.Single(service.Evaluate([Rule()], [Listing("99 z")]));
        Assert.Empty(service.Evaluate([Rule()], []));
        Assert.Empty(service.Evaluate([Rule()], [Listing("99 z")]));
    }

    [Fact]
    public void RemovingAndReaddingARuleDoesNotForgetKnownOffers()
    {
        var service = new MarketWatchService();
        Assert.Empty(service.Evaluate([Rule(maximum: 90)], [Listing()]));
        Assert.Single(service.Evaluate([Rule()], [Listing()]));
        Assert.Empty(service.Evaluate([], [Listing()]));
        Assert.Empty(service.Evaluate([Rule()], [Listing()]));
        Assert.Single(service.Evaluate([Rule()], [Listing(owner: "Bob")]));
    }

    [Fact]
    public void SeveralMatchingOffersProduceOneNotificationWithCheapestNewPrice()
    {
        var service = new MarketWatchService();
        Assert.Equal(50, Assert.Single(service.Evaluate([Rule()], [Listing(), Listing("50 z", "Bob"), Listing()])).Price);
        var text = MarketWatchService.FormatText("%name / %price / %name", new(730, "Diamond", 5));
        Assert.Equal("Diamond / 5 / Diamond", text);
    }

    [Fact]
    public void SettingsAndPresetsKeepRulesAndAppearanceWithoutRuntimeNotifications()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoAura.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(root);
            var settings = store.Load();
            Assert.Empty(settings.MarketWatchRules);
            var savedRule = Rule(); savedRule.Enabled = false;
            settings.MarketWatchRules.Add(savedRule);
            settings.MarketBackgroundRefreshMinutes = 1;
            settings.MarketWatchHistoryRetentionDays = 14;
            settings.MarketWatchNotifications.TextTemplate = "%name: %price";
            settings.MarketWatchNotifications.ShowIcon = false;
            settings.MarketWatchNotifications.SoundVolumePercent = 35;
            settings.MarketWatchNotifications.Padding = 0;
            settings.MarketWatchNotifications.X = -45.5;
            store.CreateProfile("Watch", settings);
            settings.MarketWatchRules.Clear();
            store.Save(settings);
            store.ApplyProfile("Watch");
            var restored = store.Load();
            Assert.Equal(730, Assert.Single(restored.MarketWatchRules).ItemId);
            Assert.False(restored.MarketWatchRules[0].Enabled);
            Assert.Equal(100, restored.MarketWatchRules[0].MaximumPrice);
            Assert.False(restored.MarketWatchNotifications.ShowIcon);
            Assert.Equal(35, restored.MarketWatchNotifications.SoundVolumePercent);
            Assert.Equal(1, restored.MarketBackgroundRefreshMinutes);
            Assert.Equal(14, restored.MarketWatchHistoryRetentionDays);
            Assert.Equal(0, restored.MarketWatchNotifications.Padding);
            Assert.Equal(-45.5, restored.MarketWatchNotifications.X);
            Assert.Equal("%name: %price", restored.MarketWatchNotifications.TextTemplate);
            Assert.DoesNotContain("ActiveMarketNotifications", JsonSerializer.Serialize(restored));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ConsecutiveCrawlsHaveNoTenMinuteFloorAndFailuresPreserveSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoAura.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new MarketHandler();
            var market = new SpringRoMarketService(handler, root);
            var events = 0;
            market.SnapshotUpdated += _ => events++;
            await market.RefreshSnapshotAsync();
            await market.RefreshSnapshotAsync();
            Assert.Equal(2, events);
            Assert.True(market.TryGetMinimumSellPrice(730, "Diamond", out var price));
            Assert.Equal(100, price);
            handler.Fail = true;
            await market.RefreshSnapshotAsync();
            Assert.Equal(2, events);
            Assert.True(market.TryGetMinimumSellPrice(730, "Diamond", out _));
            handler.Fail = false; handler.Empty = true;
            await market.RefreshSnapshotAsync();
            Assert.Equal(3, events);
            Assert.True(market.HasSnapshot);
            Assert.False(market.TryGetMinimumSellPrice(730, "Diamond", out _));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class MarketHandler : HttpMessageHandler
    {
        public bool Fail { get; set; }
        public bool Empty { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = Fail ? "unexpected error page" : Empty || request.RequestUri!.Query.Contains("BUY")
                ? "total:0,stallCount:0" : "total:1,stallCount:1<tr><td><span data-item-id=\"730\"></span></td><td>Diamond</td><td>0</td><td></td><td>100 z</td><td>1</td><td>Shop</td><td>Alice</td><td>location: prontera (1,2)</td></tr>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }
}
