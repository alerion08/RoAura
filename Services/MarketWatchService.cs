using RoAura.Models;

namespace RoAura.Services;

/// <summary>Consumes complete, successful live snapshots only. All calls are on the UI dispatcher.</summary>
public sealed class MarketWatchService
{
    private readonly Dictionary<int, (bool AnyPrice, long Maximum, HashSet<string> Listings)> _previous = new();

    public void Reset() => _previous.Clear();

    public void Forget(int itemId) => _previous.Remove(itemId);

    public IReadOnlyList<MarketWatchMatch> Evaluate(IEnumerable<MarketWatchRule> rules, IReadOnlyList<MarketListing> snapshot)
    {
        var currentRules = rules.Where(r => r.Enabled && r.ItemId > 0 && (r.WithoutPrice || r.MaximumPrice >= 0))
            .GroupBy(r => r.ItemId).Select(g => g.Last()).ToList();
        var ids = currentRules.Select(r => r.ItemId).ToHashSet();
        foreach (var id in _previous.Keys.Where(id => !ids.Contains(id)).ToList()) _previous.Remove(id);
        var sellById = snapshot.Where(l => l.ShopType.Equals("SELL", StringComparison.OrdinalIgnoreCase))
            .ToLookup(l => l.ItemId);
        var matches = new List<MarketWatchMatch>();
        foreach (var rule in currentRules)
        {
            var qualifying = sellById[rule.ItemId]
                .Select(l => (Listing: l, Price: SpringRoMarketService.ParsePrice(l.Price)))
                .Where(x => x.Price != long.MaxValue && (rule.WithoutPrice || x.Price <= rule.MaximumPrice)).ToList();
            var fingerprints = qualifying.Select(x => Fingerprint(x.Listing, x.Price)).ToHashSet();
            var hasPrevious = _previous.TryGetValue(rule.ItemId, out var old) &&
                              old.AnyPrice == rule.WithoutPrice && old.Maximum == rule.MaximumPrice;
            var fresh = qualifying.Where(x => !hasPrevious || !old.Listings.Contains(Fingerprint(x.Listing, x.Price)))
                .OrderBy(x => x.Price).ToList();
            if (fresh.Count > 0)
                matches.Add(new MarketWatchMatch(rule.ItemId, rule.ItemName, fresh[0].Price));
            _previous[rule.ItemId] = (rule.WithoutPrice, rule.MaximumPrice, fingerprints);
        }
        return matches;
    }

    public static string FormatText(string template, MarketWatchMatch match) =>
        template.Replace("%name", match.ItemName).Replace("%price", match.Price.ToString("N0"));

    // Quantity changes alone are not a new offer. Price, seller/location and equipment variant are.
    private static string Fingerprint(MarketListing listing, long price) =>
        System.Text.Json.JsonSerializer.Serialize(new object[]
            { listing.ItemId, listing.Owner, listing.Stall, listing.Map, listing.X, listing.Y, listing.Refine, listing.Cards, price });
}
