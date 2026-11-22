using RoAura.Models;

namespace RoAura.Services;

internal sealed record FarmingPriceChange(string Name, int ItemId, long Quantity,
    string SavedSource, long? SavedPrice, string CurrentSource, long? CurrentPrice);

internal sealed record FarmingSessionRevaluation(decimal Total, int Unpriced, IReadOnlyList<FarmingPriceChange> Changes)
{
    internal static FarmingSessionRevaluation Calculate(IEnumerable<FarmingLootEntry> loot,
        Func<int, string, (long Value, string Source, bool Known)> resolvePrice)
    {
        decimal total = 0;
        var unpriced = 0;
        var changes = new List<FarmingPriceChange>();
        foreach (var group in loot.GroupBy(row => row.ItemId))
        {
            var price = resolvePrice(group.Key, group.First().Name);
            if (price.Known) total += group.Sum(row => (decimal)row.Count) * price.Value;
            else unpriced++;

            foreach (var row in group)
            {
                var savedSource = row.Excluded ? "Excluded" : row.PriceSource;
                var savedPrice = row.Excluded ? 0L : row.PriceKnown ? row.UnitPrice : (long?)null;
                var currentPrice = price.Known ? price.Value : (long?)null;
                if (string.Equals(savedSource, price.Source, StringComparison.OrdinalIgnoreCase) &&
                    savedPrice.HasValue == currentPrice.HasValue &&
                    (!savedSource.Equals("Manual", StringComparison.OrdinalIgnoreCase) || savedPrice == currentPrice))
                    continue;
                changes.Add(new(row.Name, row.ItemId, row.Count,
                    savedSource, savedPrice, price.Source, currentPrice));
            }
        }

        return new(total, unpriced, changes);
    }
}
