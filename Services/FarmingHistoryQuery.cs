using System.Globalization;
using RoAura.Models;

namespace RoAura.Services;

internal sealed record FarmingHistoryResult(FarmingSession[] Sessions, decimal Revenue, decimal Expenses,
    decimal ItemQuantity, FarmingSession? Best)
{
    public decimal NetIncome => Revenue - Expenses;
    public decimal? MedianNetIncome
    {
        get
        {
            var amounts = Sessions.Select(row => row.NetValue).Order().ToArray();
            if (amounts.Length == 0) return null;
            var middle = amounts.Length / 2;
            return amounts.Length % 2 == 0 ? (amounts[middle - 1] + amounts[middle]) / 2 : amounts[middle];
        }
    }
}

internal static class FarmingHistoryQuery
{
    internal static FarmingHistoryResult Filter(IEnumerable<FarmingSession> sessions, DateTime? from, DateTime? through,
        string itemQuery, decimal minimumMinutes = 0, string tag = "")
    {
        var query = itemQuery.Trim();
        var matchById = int.TryParse(query.TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var itemId);
        var matches = new List<FarmingSession>();
        decimal quantity = 0, revenue = 0, expenses = 0;
        FarmingSession? best = null;
        decimal? bestValue = null;
        foreach (var session in sessions)
        {
            var date = session.StartedUtc.ToLocalTime().Date;
            if (date < from?.Date || date > through?.Date ||
                (decimal)session.ActiveTicks / TimeSpan.TicksPerMinute < minimumMinutes) continue;
            if (tag.Length > 0 && !(session.Tags ?? "").Split(',', StringSplitOptions.TrimEntries)
                .Any(value => value.Contains(tag, StringComparison.CurrentCultureIgnoreCase))) continue;

            if (query.Length > 0)
            {
                var matchedQuantity = session.Loot.Where(loot => matchById ? loot.ItemId == itemId :
                    loot.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Sum(loot => (decimal)loot.Count);
                if (matchedQuantity <= 0) continue;
                quantity += matchedQuantity;
            }

            matches.Add(session);
            var value = (decimal)session.ZenyGained + session.LootValue;
            revenue += value;
            expenses += session.ExpensesZeny;
            if (bestValue is null || value > bestValue)
            {
                best = session;
                bestValue = value;
            }
        }

        return new FarmingHistoryResult(matches.ToArray(), revenue, expenses, quantity, best);
    }
}
