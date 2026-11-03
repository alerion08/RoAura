using RoAura.Models;

namespace RoAura.Services;

/// <summary>One quiet interval for a group's loot, experience and Zeny notifications.</summary>
public static class RewardAggregation
{
    public static void Refresh(RewardGroupSettings group, DateTime nowUtc)
    {
        var interval = TimeSpan.FromMilliseconds(group.AggregationWindowMs);
        foreach (var row in group.Notifications.Where(row =>
                     nowUtc - row.LastUpdatedUtc <= interval && row.ExpiresAtUtc > nowUtc))
        {
            row.LastUpdatedUtc = nowUtc;
            row.ExpiresAtUtc = nowUtc.AddSeconds(group.DurationSeconds);
        }
    }
}
