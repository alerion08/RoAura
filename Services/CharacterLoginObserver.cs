using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace RoAura.Services;

/// <summary>
/// DEV-only, passive candidate detection for the FIRST client-to-map-server
/// payload of a TCP flow. This never changes the player AID, session state,
/// reward counters or existing server-to-client packet scanner.
/// This is NOT proof that a candidate was accepted by the map server.
/// </summary>
public sealed record CharacterLoginCandidate(ushort PacketId, uint CharacterId,
    int PacketLength, uint PlayerAid = 0); // transient in-memory AID, never logged

public sealed class CharacterLoginStreamProbe
{
    private const int MaximumPrefixLength = 23;
    private readonly object _sync = new();
    private readonly byte[] _prefix = new byte[MaximumPrefixLength];
    private int _prefixLength;
    private bool _initialized;
    private bool _finished;
    private bool _identified;
    private ushort? _observedOpcode;
    private uint _nextSequence;
    private long _lastSeen = Environment.TickCount64;

    public long LastSeenTick => Volatile.Read(ref _lastSeen);
    public ushort? FirstOpcode
    {
        get
        {
            lock (_sync) return _observedOpcode;
        }
    }
    public bool Finished { get { lock (_sync) return _finished; } }
    public bool Identified { get { lock (_sync) return _identified; } }

    /// <summary>Returns one candidate at most. Duplicates and gaps cannot produce another.</summary>
    public CharacterLoginCandidate? Process(uint sequence, ReadOnlySpan<byte> payload)
    {
        Volatile.Write(ref _lastSeen, Environment.TickCount64);
        if (payload.IsEmpty) return null;

        lock (_sync)
        {
            if (_finished) return null;
            if (_initialized)
            {
                var delta = unchecked((int)(sequence - _nextSequence));
                if (delta < 0)
                {
                    var overlap = unchecked(_nextSequence - sequence);
                    if (overlap >= payload.Length) return null;
                    sequence = _nextSequence;
                    payload = payload[(int)overlap..];
                }
                else if (delta != 0)
                {
                    // This observer cannot reconstruct a trustworthy login from
                    // a missing segment or a reused TCP sequence. Fail closed.
                    Finish();
                    return null;
                }
            }
            _initialized = true;
            _nextSequence = unchecked(sequence + (uint)payload.Length);

            var copied = Math.Min(payload.Length, MaximumPrefixLength - _prefixLength);
            if (copied > 0)
            {
                payload[..copied].CopyTo(_prefix.AsSpan(_prefixLength));
                _prefixLength += copied;
            }

            if (_prefixLength < 2) return null;
            var opcode = BinaryPrimitives.ReadUInt16LittleEndian(_prefix);
            _observedOpcode = opcode;
            if (opcode is not (0x0072 or 0x0436))
            {
                Finish(); // Mid-stream attachment or unknown shuffled opcode.
                return null;
            }
            if (_prefixLength < 19) return null;
            var aid = BinaryPrimitives.ReadUInt32LittleEndian(_prefix.AsSpan(2));
            var cid = BinaryPrimitives.ReadUInt32LittleEndian(_prefix.AsSpan(6));
            if (aid == 0 || cid == 0)
            {
                Finish();
                return null;
            }

            // Standard map-login has 19-byte and (some packet versions) 23-byte
            // variants. Never read/authenticate/log the login auth code.
            // The initial 19-byte candidate can be ambiguous for a 23-byte
            // packet whose timestamp happens to end in 0/1. This diagnostic
            // only reports the CID shared by both layouts, not the variant.
            var length = _prefix[18] <= 1 ? 19 : 0;
            if (length == 0 && _prefixLength >= 23 && _prefix[22] <= 1)
                length = 23;
            if (length == 0 && _prefixLength < 23) return null;
            _identified = length != 0;
            Finish();
            return length == 0 ? null : new CharacterLoginCandidate(opcode, cid, length, aid);
        }
    }

    private void Finish()
    {
        _finished = true;
        // Do not retain the short-lived map-login auth token in a flow cache.
        _prefix.AsSpan().Clear();
    }
}

/// <summary>
/// One observer per capture generation. Shares first-payload evidence across
/// duplicate Npcap adapters, bounded by flow count and idle expiration.
/// Never writes raw traffic, account ID, auth codes or client IPs to disk.
/// </summary>
public sealed class CharacterLoginObserver
{
    private const int MaximumFlows = 512;
    private const long IdleMs = 2 * 60 * 1000;
    private const long DuplicateDiagnosticWindowMs = 2_000;
    // Across different adapters the same packet can use different IP tuples.
    // Duplicate suppression is for LOG OUTPUT only: it does not change parsing.
    // Static scope also handles overlapping capture generations after retargeting.
    private static readonly object DiagnosticSync = new();
    private static readonly Dictionary<(ushort Opcode, uint Cid, ushort ClientPort, ushort MapPort), long>
        RecentDiagnostics = new();
    private readonly ConcurrentDictionary<TcpFlowKey, CharacterLoginStreamProbe> _flows = new();
    internal event Action<TcpFlowKey, CharacterLoginCandidate>? CandidateDetected;
    private readonly object _seenLock = new();
    private readonly HashSet<(ushort Opcode, uint Cid, ushort ClientPort, ushort MapPort)>
        _seenThisGeneration = [];
    private int _unrecognizedLogged;

    internal void Observe(TcpFlowKey key, uint sequence, ReadOnlySpan<byte> payload, bool closing)
    {
        if (closing)
        {
            _flows.TryRemove(key, out _);
            return;
        }
        if (payload.IsEmpty) return;
        if (_flows.Count >= MaximumFlows)
            Prune();
        if (_flows.Count >= MaximumFlows) return;
        var probe = _flows.GetOrAdd(key, _ => new CharacterLoginStreamProbe());
        var candidate = probe.Process(sequence, payload);
        if (candidate is null)
        {
            // Limited, metadata-only hint that this client's packetver may
            // shuffle map-login IDs, or that capture began mid-connection.
            if (probe.Finished && !probe.Identified && probe.FirstOpcode is ushort opcode &&
                Interlocked.Increment(ref _unrecognizedLogged) <= 10)
                DebugLog.Write($"CHARACTER MAP LOGIN NOT IDENTIFIED: firstOpcode=0x{opcode:X4}; " +
                               $"mapPort={key.DestinationPort}; check packetver / late capture");
            return;
        }
        var identity = (candidate.PacketId, candidate.CharacterId,
            key.SourcePort, key.DestinationPort);
        lock (_seenLock)
        {
            if (!_seenThisGeneration.Add(identity)) return;
            if (_seenThisGeneration.Count > MaximumFlows) _seenThisGeneration.Clear();
        }
        // Log suppression is process-global, identity delivery is per-generation.
        // A filter restart must never lose a CID due to a recent diagnostic log.
        CandidateDetected?.Invoke(key, candidate);
        if (!ShouldLogCandidate(candidate, key)) return;
        DebugLog.Write($"CHARACTER LOGIN CANDIDATE (unconfirmed): opcode=0x{candidate.PacketId:X4}; " +
                       $"cid={candidate.CharacterId}; length={candidate.PacketLength}; " +
                       $"mapPort={key.DestinationPort}; clientPort={key.SourcePort}");
    }

    private static bool ShouldLogCandidate(CharacterLoginCandidate candidate, TcpFlowKey flow)
    {
        var key = (candidate.PacketId, candidate.CharacterId,
            flow.SourcePort, flow.DestinationPort);
        lock (DiagnosticSync)
        {
            var now = Environment.TickCount64;
            if (RecentDiagnostics.TryGetValue(key, out var previous) &&
                now - previous < DuplicateDiagnosticWindowMs)
                return false;

            // A log-only cache must never grow indefinitely during lengthy play.
            if (RecentDiagnostics.Count >= 128)
            {
                foreach (var expired in RecentDiagnostics
                             .Where(pair => now - pair.Value > DuplicateDiagnosticWindowMs)
                             .Select(pair => pair.Key).ToArray())
                    RecentDiagnostics.Remove(expired);
                if (RecentDiagnostics.Count >= 128) RecentDiagnostics.Clear();
            }
            RecentDiagnostics[key] = now;
            return true;
        }
    }

    private void Prune()
    {
        var now = Environment.TickCount64;
        foreach (var pair in _flows)
            if (now - pair.Value.LastSeenTick > IdleMs)
                _flows.TryRemove(pair.Key, out _);
    }
}
