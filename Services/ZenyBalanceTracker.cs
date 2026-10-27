namespace RoAura.Services;

/// <summary>
/// Converts absolute Zeny balance packets into trusted positive gains.
/// A single implausibly large balance jump is held until a following packet
/// confirms the new range, preventing an accidental packet match from
/// poisoning the whole statistics session.
/// </summary>
public sealed class ZenyBalanceTracker
{
    public const long MaxUnconfirmedChange = 100_000_000;

    private long? _acceptedTotal;
    private long? _pendingTotal;

    public long? AcceptedTotal => _acceptedTotal;

    public void Reset()
    {
        _acceptedTotal = null;
        _pendingTotal = null;
    }

    public ZenyBalanceSample Observe(long total)
    {
        if (total < 0 || total > 2_000_000_000)
            return new(false, 0, true, "balance outside the supported range");

        if (!_acceptedTotal.HasValue)
        {
            _acceptedTotal = total;
            _pendingTotal = null;
            return new(true, 0, false, "initial balance");
        }

        var change = total - _acceptedTotal.Value;
        if (Math.Abs(change) <= MaxUnconfirmedChange)
        {
            _acceptedTotal = total;
            _pendingTotal = null;
            return new(true, Math.Max(0, change), false, "normal balance change");
        }

        if (_pendingTotal.HasValue &&
            Math.Abs(total - _pendingTotal.Value) <= MaxUnconfirmedChange)
        {
            var confirmedGain = Math.Max(0, total - _acceptedTotal.Value);
            _acceptedTotal = total;
            _pendingTotal = null;
            return new(true, confirmedGain, false, "large balance change confirmed");
        }

        _pendingTotal = total;
        return new(false, 0, true, "large balance change awaiting confirmation");
    }
}

public readonly record struct ZenyBalanceSample(
    bool Accepted,
    long Gained,
    bool Suspicious,
    string Reason);
