using RoAura.Models;

namespace RoAura.Services;

/// <summary>Consumes complete successful live snapshots on the UI dispatcher. History survives absence/restarts.</summary>
public sealed class MarketWatchService
{
    private readonly MarketWatchHistoryStore? _store;
    private readonly Func<DateTime> _utcNow;
    private readonly List<MarketWatchHistoryEntry> _history;
    private readonly HashSet<string> _seen;
    public event Action? HistoryChanged;
    public IReadOnlyList<MarketWatchHistoryEntry> History => _history.AsReadOnly();
    public bool HistoryPersistenceFailed => _store?.HasError == true;

    public MarketWatchService(MarketWatchHistoryStore? store = null, Func<DateTime>? utcNow = null)
    {
        _store = store;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _history = (store?.Load() ?? []).GroupBy(e => e.Key).Select(g => g.OrderByDescending(e => e.DetectedAtUtc).First()).ToList();
        _seen = _history.Select(e => e.Key).ToHashSet();
    }

    public void PruneHistory(int retentionDays)
    {
        if (!Prune(retentionDays)) return;
        PersistAndNotify();
    }

    private bool Prune(int retentionDays)
    {
        var cutoff = _utcNow().AddDays(-Math.Clamp(retentionDays, 1, 3650));
        var removed = _history.RemoveAll(e => e.DetectedAtUtc <= cutoff) > 0;
        if (removed) { _seen.Clear(); foreach (var entry in _history) _seen.Add(entry.Key); }
        return removed;
    }

    public IReadOnlyList<MarketWatchMatch> Evaluate(IEnumerable<MarketWatchRule> rules, IReadOnlyList<MarketListing> snapshot, int retentionDays = 30)
    {
        var changed = Prune(retentionDays);
        var currentRules = rules.Where(r => r.Enabled && r.ItemId > 0 && (r.WithoutPrice || r.MaximumPrice >= 0))
            .GroupBy(r => r.ItemId).Select(g => g.Last()).ToList();
        var sellById = snapshot.Where(l => l.ShopType.Equals("SELL", StringComparison.OrdinalIgnoreCase)).ToLookup(l => l.ItemId);
        var matches = new List<MarketWatchMatch>();
        foreach (var rule in currentRules)
        {
            long? cheapestNewPrice = null;
            foreach (var listing in sellById[rule.ItemId])
            {
                var price = SpringRoMarketService.ParsePrice(listing.Price);
                if (price == long.MaxValue || (!rule.WithoutPrice && price > rule.MaximumPrice)) continue;
                var entry = new MarketWatchHistoryEntry
                {
                    DetectedAtUtc = _utcNow(), ItemId = rule.ItemId, ItemName = rule.ItemName,
                    Stall = listing.Stall, Owner = listing.Owner, Map = listing.Map, X = listing.X, Y = listing.Y,
                    Price = price, Refine = listing.Refine, Cards = listing.Cards
                };
                if (!_seen.Add(entry.Key)) continue;
                _history.Add(entry);
                changed = true;
                cheapestNewPrice = cheapestNewPrice is null ? price : Math.Min(cheapestNewPrice.Value, price);
            }
            if (cheapestNewPrice.HasValue) matches.Add(new(rule.ItemId, rule.ItemName, cheapestNewPrice.Value));
        }
        // Write before presenting alerts, so a normal restart cannot rediscover the same offers.
        if (changed || HistoryPersistenceFailed) PersistAndNotify();
        return matches;
    }

    private void PersistAndNotify()
    {
        _store?.Save(_history);
        HistoryChanged?.Invoke();
    }

    public static string FormatText(string template, MarketWatchMatch match) =>
        template.Replace("%name", match.ItemName).Replace("%price", match.Price.ToString("N0"));
}
