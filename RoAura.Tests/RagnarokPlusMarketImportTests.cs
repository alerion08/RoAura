using System.IO;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class RagnarokPlusMarketImportTests
{
    private static readonly DatabaseItem[] Catalog = [new() { Id = 999, Name = "Steel" }];
    private const string SteelMarket = """
        Steel - Live Market
        1 selling
        Selling
        Shop
        Qty
        Price
        Nippo
        SC
        Clips / SteeL
        prt_mkp (111, 129)
        x194
        11,000
        z
        prt_mkp
        """;

    [Fact]
    public void LiveMarketTextResolvesTheItemAndReadsTheSellingOffer()
    {
        var result = RagnarokPlusData.ParseLiveMarket(SteelMarket, Catalog);
        var row = Assert.Single(result.Snapshot.Listings);
        Assert.Equal(999, result.ItemId);
        Assert.Equal(("SC", "SELL", "prt_mkp", 111, 129),
            (row.GameMode, row.ShopType, row.Map, row.X, row.Y));
        Assert.Equal("194", row.Quantity);
        Assert.Equal("11,000", row.Price);
    }

    [Fact]
    public void PartialCopyDoesNotReplaceAnItemsOffers()
    {
        Assert.Throws<InvalidDataException>(() =>
            RagnarokPlusData.ParseLiveMarket(SteelMarket.Replace("1 selling", "2 selling"), Catalog));
    }
}
