using System.Numerics;
using RoAura.Models;

namespace RoAura.Services;

internal static class MarketPriceDepth
{
    internal sealed record Variant(int ItemId, string ItemName, string GameMode, string Refine, string Cards, PriceLevel[] Levels)
    {
        internal (int ItemId, string Mode, string Refine, string Cards) Key => (ItemId, GameMode, Refine, Cards);
    }
    internal sealed record PriceLevel(long Price, BigInteger Quantity);

    internal static Variant[] Build(IEnumerable<MarketListing> listings, bool buying)
    {
        var shopType = buying ? "BUY" : "SELL";
        return listings.Where(listing => listing.ShopType.Equals(shopType, StringComparison.OrdinalIgnoreCase))
            .Select(listing => (Listing: listing,
                Price: SpringRoMarketService.ParsePrice(listing.Price),
                Quantity: SpringRoMarketService.ParsePrice(listing.Quantity)))
            .Where(offer => offer.Price is > 0 and < long.MaxValue && offer.Quantity is > 0 and < long.MaxValue)
            .GroupBy(offer => (offer.Listing.ItemId, offer.Listing.ItemName, offer.Listing.GameMode, offer.Listing.Refine, offer.Listing.Cards))
            .Select(group =>
            {
                var levels = group.GroupBy(offer => offer.Price).Select(priceGroup => new PriceLevel(priceGroup.Key,
                    priceGroup.Aggregate(BigInteger.Zero, (total, offer) => total + offer.Quantity)));
                return new Variant(group.Key.ItemId, group.Key.ItemName, group.Key.GameMode, group.Key.Refine, group.Key.Cards,
                    (buying ? levels.OrderByDescending(level => level.Price) : levels.OrderBy(level => level.Price)).ToArray());
            }).ToArray();
    }
}
