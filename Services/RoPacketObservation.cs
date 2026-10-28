namespace RoAura.Services;

/// <summary>In-memory lifetime of one inbound TCP stream, never serialized.</summary>
public sealed class RoPacketSource(int generation, CharacterSessionFlow flow)
{
    private int _active = 1;
    public int Generation { get; } = generation;
    public CharacterSessionFlow Flow { get; } = flow;
    public bool IsActive => Volatile.Read(ref _active) != 0;
    public void Close() => Interlocked.Exchange(ref _active, 0);
}

/// <summary>Decode first; retain the source and time across UI dispatch and learning.</summary>
public readonly record struct RoPacketObservation<T>(
    T Packet, RoPacketSource Source, DateTime ObservedAtUtc, uint SelfAid, string IdentitySource)
{
    public bool IsOwnActor(uint actor) => SelfAid != 0 && SelfAid == actor;
}

/// <summary>OPTION_HIDE belongs to an actor, not to all actors on a connection.</summary>
public sealed class HidingStateTracker
{
    private readonly Dictionary<uint, bool> _hidden = [];

    public bool Observe(RoOptionPacket packet)
    {
        var wasHidden = _hidden.TryGetValue(packet.Aid, out var previous) && previous;
        // Keep only active actors, with a bound for malformed/untrusted streams.
        if (packet.IsHidden)
        {
            if (_hidden.Count < 4096 || _hidden.ContainsKey(packet.Aid))
                _hidden[packet.Aid] = true;
        }
        else _hidden.Remove(packet.Aid);
        return packet.IsHidden && !wasHidden;
    }

    public void Reset() => _hidden.Clear();
}

/// <summary>
/// Keep a small, short-lived set of undecided statuses until this same flow
/// supplies explicit self identity. Only the newest state per actor/status is
/// replayed, with its original time, so identity discovery cannot extend a buff.
/// </summary>
public sealed class PendingStatusObservations
{
    private readonly Dictionary<(uint, ushort), RoPacketObservation<RoStatusPacket>> _pending = [];
    public void Remember(RoPacketObservation<RoStatusPacket> observation)
    {
        var key = (observation.Packet.Aid, observation.Packet.StatusId);
        if (_pending.Count >= 256 && !_pending.ContainsKey(key)) _pending.Remove(_pending.Keys.First());
        _pending[key] = observation;
    }

    public IReadOnlyList<RoPacketObservation<RoStatusPacket>> Resolve(RoPacketSource source,
        uint selfAid, string identitySource, DateTime now)
    {
        if (selfAid == 0 || !source.IsActive) return [];
        var result = _pending.Values.Where(o => ReferenceEquals(o.Source, source) &&
            o.Packet.Aid == selfAid && now - o.ObservedAtUtc <= TimeSpan.FromMinutes(2))
            .OrderBy(o => o.ObservedAtUtc)
            .Select(o => o with { SelfAid = selfAid, IdentitySource = identitySource }).ToList();
        _pending.Clear();
        return result;
    }
    public void Reset() => _pending.Clear();
}
