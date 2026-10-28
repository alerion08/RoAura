namespace RoAura.Services;

/// <summary>
/// A read-only shadow state machine for map-server character connections.
/// No gameplay event is routed or recorded through this manager yet.
/// A map-login CID is provisional: inbound traffic means the flow is alive,
/// not that server authentication or character ownership was proven.
/// </summary>
public enum CharacterSessionPhase { Unknown, Connecting, ActiveObserved, Suspended }

/// <summary>Full transport tuple retained in memory only. Never log network addresses.</summary>
public readonly record struct CharacterSessionFlow(
    uint ClientAddress, ushort ClientPort, uint ServerAddress, ushort ServerPort)
{
    internal static CharacterSessionFlow FromOutbound(TcpFlowKey key) =>
        new(key.SourceAddress, key.SourcePort, key.DestinationAddress, key.DestinationPort);

    internal static CharacterSessionFlow FromInbound(TcpFlowKey key) =>
        new(key.DestinationAddress, key.DestinationPort, key.SourceAddress, key.SourcePort);
}

public readonly record struct CharacterSessionSnapshot(
    string ServerKey, uint? CharacterId, CharacterSessionPhase Phase,
    ushort ClientPort, ushort MapPort, long Revision = 0);

public readonly record struct CharacterSessionPacket<T>(
    CharacterSessionSnapshot Session, T Packet);

public sealed class CharacterSessionManager
{
    private readonly object _sync = new();
    private CharacterSessionFlow? _currentFlow;
    private long _revision;
    private CharacterSessionSnapshot _current = new("", null, CharacterSessionPhase.Unknown, 0, 0);

    public event Action<CharacterSessionSnapshot>? StateChanged;

    public bool TryResolve(CharacterSessionFlow flow, out CharacterSessionSnapshot snapshot)
    {
        lock (_sync)
        {
            snapshot = _current;
            return _currentFlow == flow && snapshot.CharacterId is > 0 &&
                snapshot.Phase == CharacterSessionPhase.ActiveObserved;
        }
    }

    public bool IsCurrent(CharacterSessionSnapshot snapshot)
    {
        lock (_sync)
            return _current.Revision == snapshot.Revision &&
                _current.Phase == CharacterSessionPhase.ActiveObserved &&
                _current.CharacterId == snapshot.CharacterId &&
                _current.ServerKey == snapshot.ServerKey &&
                _current.ClientPort == snapshot.ClientPort &&
                _current.MapPort == snapshot.MapPort;
    }


    public CharacterSessionSnapshot Current
    {
        get { lock (_sync) return _current; }
    }

    /// <summary>Start observing a new candidate, suspending any earlier flow first.</summary>
    public void OnCandidate(string serverKey, CharacterSessionFlow flow, uint candidateCid)
    {
        if (candidateCid == 0 || string.IsNullOrWhiteSpace(serverKey)) return;
        CharacterSessionSnapshot? suspended = null;
        CharacterSessionSnapshot? next = null;
        lock (_sync)
        {
            if (_currentFlow == flow && _current.CharacterId == candidateCid &&
                _current.ServerKey == serverKey &&
                _current.Phase is CharacterSessionPhase.Connecting or CharacterSessionPhase.ActiveObserved)
                return;

            if (_current.Phase is CharacterSessionPhase.Connecting or CharacterSessionPhase.ActiveObserved)
                suspended = _current with { Phase = CharacterSessionPhase.Suspended, Revision = ++_revision };

            _currentFlow = flow;
            _current = new CharacterSessionSnapshot(serverKey, candidateCid,
                CharacterSessionPhase.Connecting, flow.ClientPort, flow.ServerPort, ++_revision);
            next = _current;
        }
        if (suspended is { } previous) StateChanged?.Invoke(previous);
        if (next is { } current) StateChanged?.Invoke(current);
    }

    /// <summary>
    /// Inbound game data proves network activity on the same transport flow.
    /// It does NOT authenticate the candidate. Therefore only shadow state changes.
    /// </summary>
    public void OnServerPayload(CharacterSessionFlow flow)
    {
        CharacterSessionSnapshot? updated = null;
        lock (_sync)
        {
            if (_currentFlow != flow || _current.Phase != CharacterSessionPhase.Connecting)
                return;
            _current = _current with { Phase = CharacterSessionPhase.ActiveObserved };
            updated = _current;
        }
        if (updated is { } snapshot) StateChanged?.Invoke(snapshot);
    }

    /// <summary>A late FIN/RST from an older character must never suspend a newer one.</summary>
    public void OnConnectionClosed(CharacterSessionFlow flow)
    {
        CharacterSessionSnapshot? updated = null;
        lock (_sync)
        {
            if (_currentFlow != flow || _current.Phase is CharacterSessionPhase.Unknown or CharacterSessionPhase.Suspended)
                return;
            _current = _current with { Phase = CharacterSessionPhase.Suspended, Revision = ++_revision };
            _currentFlow = null;
            updated = _current;
        }
        if (updated is { } snapshot) StateChanged?.Invoke(snapshot);
    }

    /// <summary>Capture retarget, disable or restart removes any live-flow identity.</summary>
    public void OnCaptureInterrupted()
    {
        CharacterSessionSnapshot? updated = null;
        lock (_sync)
        {
            if (_current.Phase is CharacterSessionPhase.Connecting or CharacterSessionPhase.ActiveObserved)
            {
                _current = _current with { Phase = CharacterSessionPhase.Suspended, Revision = ++_revision };
                updated = _current;
            }
            _currentFlow = null;
        }
        if (updated is { } snapshot) StateChanged?.Invoke(snapshot);
    }
}
