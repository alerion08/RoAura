using RoAura.Services;

namespace RoAura.Tests;

public sealed class ZenyBalanceTrackerTests
{
    [Fact]
    public void InitialBalanceDoesNotCountAsIncome()
    {
        var tracker = new ZenyBalanceTracker();

        var sample = tracker.Observe(1_000_000);

        Assert.True(sample.Accepted);
        Assert.Equal(0, sample.Gained);
    }

    [Fact]
    public void NormalPositiveChangeCountsAsIncome()
    {
        var tracker = new ZenyBalanceTracker();
        tracker.Observe(1_000_000);

        var sample = tracker.Observe(1_012_500);

        Assert.True(sample.Accepted);
        Assert.Equal(12_500, sample.Gained);
    }

    [Fact]
    public void SpendingUpdatesBalanceWithoutCountingIncome()
    {
        var tracker = new ZenyBalanceTracker();
        tracker.Observe(1_000_000);

        var spent = tracker.Observe(900_000);
        var gained = tracker.Observe(925_000);

        Assert.Equal(0, spent.Gained);
        Assert.Equal(25_000, gained.Gained);
    }

    [Fact]
    public void SingleHugeFalseMatchDoesNotPolluteSession()
    {
        var tracker = new ZenyBalanceTracker();
        tracker.Observe(1_000_000);

        var falseMatch = tracker.Observe(1_900_000_000);
        var realPacket = tracker.Observe(1_005_000);

        Assert.False(falseMatch.Accepted);
        Assert.True(falseMatch.Suspicious);
        Assert.True(realPacket.Accepted);
        Assert.Equal(5_000, realPacket.Gained);
    }

    [Fact]
    public void RepeatedLargeChangeIsAcceptedAsConfirmed()
    {
        var tracker = new ZenyBalanceTracker();
        tracker.Observe(10_000_000);
        tracker.Observe(510_000_000);

        var confirmed = tracker.Observe(510_000_000);

        Assert.True(confirmed.Accepted);
        Assert.Equal(500_000_000, confirmed.Gained);
    }
}
