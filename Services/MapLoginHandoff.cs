namespace RoAura.Services;

/// <summary>Short-lived evidence for an auto-detected endpoint that has not been selected yet.
/// Calls are serialized by the capture lifecycle lock. No packet bytes are retained.</summary>
internal sealed class MapLoginHandoff
{
    private const int Limit = 256;
    private const int LifetimeMs = 15_000;
    private readonly Dictionary<TcpFlowKey, Entry> _entries = new();

    private readonly record struct Entry(uint FirstSequence, long SeenAt, CharacterLoginCandidate? Login = null);

    public void Clear() => _entries.Clear();

    public void Observe(TcpFlowKey flow, uint sequence, byte flags, ReadOnlySpan<byte> payload,
        Func<TcpFlowKey, bool> isOwned, long now)
    {
        if ((flags & 0x05) != 0)
        {
            _entries.Remove(flow);
            _entries.Remove(new(flow.DestinationAddress, flow.DestinationPort, flow.SourceAddress, flow.SourcePort));
            return;
        }

        if ((flags & 0x12) == 0x02)
        {
            Prune(now);
            var firstSequence = unchecked(sequence + 1);
            if (_entries.TryGetValue(flow, out var existing) && existing.FirstSequence == firstSequence)
                return;
            if (_entries.Count < Limit || _entries.ContainsKey(flow))
                _entries[flow] = new(firstSequence, now);
            return;
        }

        if (!_entries.TryGetValue(flow, out var entry) || entry.Login is not null ||
            now - entry.SeenAt > LifetimeMs || sequence != entry.FirstSequence ||
            payload.Length is not (19 or 23))
            return;

        if (new CharacterLoginStreamProbe().Process(sequence, payload) is { } login && isOwned(flow))
            _entries[flow] = entry with { Login = login, SeenAt = now };
    }

    public (TcpFlowKey Flow, CharacterLoginCandidate Login)? Take(Func<uint, ushort, bool> matches,
        Func<TcpFlowKey, bool> isOwned, long now)
    {
        Prune(now);
        (TcpFlowKey, CharacterLoginCandidate)? result = null;
        var ambiguous = false;
        foreach (var (flow, entry) in _entries.ToArray())
        {
            if (!matches(flow.DestinationAddress, flow.DestinationPort)) continue;
            _entries.Remove(flow);
            if (entry.Login is null || !isOwned(flow)) continue;
            ambiguous |= result.HasValue;
            result = (flow, entry.Login);
        }
        return ambiguous ? null : result;
    }

    private void Prune(long now)
    {
        foreach (var (flow, entry) in _entries)
            if (now - entry.SeenAt > LifetimeMs)
                _entries.Remove(flow);
    }
}
