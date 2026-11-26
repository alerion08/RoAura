namespace RoAura.Services;

/// <summary>Bounded, metadata-only trace around inventory additions on one inbound stream.</summary>
internal sealed class LootSourceDiagnostics
{
    private const int MaxCandidates = 32;
    private const int MaxAdditions = 32;
    private const int WindowMilliseconds = 1200;
    private static int _nextTraceId;

    internal static bool Enabled { get; } = AppEnvironment.IsDevelopment &&
        Environment.GetCommandLineArgs().Contains("--diagnose-loot-source", StringComparer.OrdinalIgnoreCase);

    private readonly int _traceId = Interlocked.Increment(ref _nextTraceId);
    private readonly Queue<Candidate> _recent = new(MaxCandidates);
    private int _additions;
    private int _remainingAfter;
    private long _lastAdditionTime;

    internal void Observe(ushort opcode, int length, long offset, bool boundary, int skillId)
    {
        if (_additions >= MaxAdditions && _remainingAfter == 0) return;

        var time = Environment.TickCount64;
        var candidate = new Candidate(time, opcode, length, offset, boundary, skillId);
        if (_recent.Count == MaxCandidates) _recent.Dequeue();
        _recent.Enqueue(candidate);

        if (_remainingAfter == 0 || opcode == 0x0B41) return;
        if (time - _lastAdditionTime > WindowMilliseconds)
        {
            _remainingAfter = 0;
            return;
        }

        _remainingAfter--;
        DebugLog.Write($"LOOT SOURCE DIAG stream={_traceId}; event={_additions}; " +
                       $"after={candidate.Describe(_lastAdditionTime)}");
    }

    internal void ItemAdded(RoItemAddedPacket packet, long offset)
    {
        if (_additions >= MaxAdditions) return;

        _additions++;
        _lastAdditionTime = Environment.TickCount64;
        _remainingAfter = MaxCandidates;
        var before = _recent.Where(candidate => candidate.Offset < offset &&
            _lastAdditionTime - candidate.Time <= WindowMilliseconds)
            .Select(candidate => candidate.Describe(_lastAdditionTime));
        DebugLog.Write($"LOOT SOURCE DIAG stream={_traceId}; event={_additions}; " +
                       $"item={packet.ItemId}; count={packet.Count}; slot={packet.InventoryIndex}; " +
                       $"offset={offset}; before=[{string.Join(" | ", before)}]");
    }

    internal void ResetStream()
    {
        _recent.Clear();
        _remainingAfter = 0;
    }

    private readonly record struct Candidate(long Time, ushort Opcode, int Length,
        long Offset, bool Boundary, int SkillId)
    {
        internal string Describe(long anchor) =>
            $"0x{Opcode:X4} len={Length} offset={Offset} dt={Time - anchor}ms " +
            $"boundary={Boundary} skill={SkillId}";
    }
}
