using System.Buffers.Binary;

namespace RoAura.Services;

/// <summary>Selected scalar fields only. Actor values remain in memory and are labelled before logging.</summary>
public sealed record RoPacketLayoutProbe(ushort Opcode, long Offset, bool Boundary,
    uint Actor, uint AlternateActor, string Fields)
{
    // DEV-only transient scalar candidates, never printed as raw values.
    // Offsets 0..N refer to the start of this candidate frame.
    public ushort[]? CandidateWords { get; init; }
    public uint[]? CandidateDwords { get; init; }
    public ushort SkillId { get; init; }
    public bool SkillSucceeded { get; init; }
    // The last byte of a 9-byte 0x0196 has no U16 window; record it
    // explicitly instead of trying to read CandidateWords[8].
    public byte? StatusState { get; init; }
    public uint StatusTotalMs { get; init; }
    public uint StatusLeftMs { get; init; }
    public static RoPacketLayoutProbe? Create(ReadOnlySpan<byte> frame, long offset, bool boundary)
    {
        if (frame.Length < 2) return null;
        var opcode = BinaryPrimitives.ReadUInt16LittleEndian(frame);
        static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b[at..]);
        static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b[at..]);
        uint actor, alternate = 0;
        string fields;
        switch (opcode)
        {
            case 0x0119 when frame.Length == 13:
            case 0x0229 when frame.Length == 15:
                actor = U32(frame, 2);
                var mask = opcode == 0x0229 ? U32(frame, 10) : U16(frame, 10);
                fields = $"body=0x{U16(frame, 6):X4}; health=0x{U16(frame, 8):X4}; option=0x{mask:X8}; karma={frame[^1]}; hideBit={(mask & 2) != 0}";
                break;
            case 0x0983 when frame.Length == 29:
            case 0x043F when frame.Length == 25:
            case 0x0196 when frame.Length == 9:
                actor = U32(frame, 4);
                alternate = U32(frame, 2);
                fields = $"status={U16(frame, 2)}; state={frame[8]}";
                if (frame.Length > 9)
                    fields += $"; total={U32(frame, 9)}; left={U32(frame, opcode == 0x0983 ? 13 : 9)}";
                break;
            case 0x08FF when frame.Length == 24:
            case 0x0984 when frame.Length == 28:
                actor = U32(frame, 2);
                alternate = U32(frame, 4);
                fields = $"status={U16(frame, 6)}; total={U32(frame, 8)}; left={U32(frame, opcode == 0x0984 ? 12 : 8)}";
                break;
            case 0x011A when frame.Length == 15:
            case 0x09CB when frame.Length == 17:
                var shift = opcode == 0x09CB ? 2 : 0;
                actor = U32(frame, 10 + shift);
                alternate = U32(frame, 6 + shift); // target, not evidence of self
                fields = $"skill={U16(frame, 2)}; amount={(shift == 2 ? U32(frame, 4) : U16(frame, 4))}; result={frame[^1]}";
                break;
            default: return null;
        }
        var probe = new RoPacketLayoutProbe(opcode, offset, boundary, actor, alternate, fields);
        if (opcode == 0x09CB)
            return probe with { SkillId = U16(frame, 2), SkillSucceeded = frame[^1] == 1 };
        if (opcode is 0x0983 or 0x0196)
        {
            // Preserve only scalar candidates transiently for equality tests.
            // In particular, never log these unverified values as actor IDs.
            var words = new ushort[frame.Length - 1];
            var dwords = new uint[frame.Length - 3];
            for (var i = 0; i < words.Length; i++) words[i] = U16(frame, i);
            for (var i = 0; i < dwords.Length; i++) dwords[i] = U32(frame, i);
            return probe with
            {
                CandidateWords = words, CandidateDwords = dwords,
                StatusState = frame[8],
                StatusTotalMs = opcode == 0x0983 ? U32(frame, 9) : 0,
                StatusLeftMs = opcode == 0x0983 ? U32(frame, 13) : 0
            };
        }
        return probe;
    }
}

/// <summary>Bounded, rearmable DEV investigation; no packet dumps, IPs or account/character IDs.</summary>
public sealed class PacketLayoutDiagnostics
{
    private readonly object _sync = new();
    private readonly Dictionary<uint, int> _actors = [];
    private readonly Dictionary<RoPacketSource, int> _flows = [];
    private DateTime _until;
    private int _count;
    private int _window;

    public void Begin(DateTime now)
    {
        lock (_sync)
        {
            _until = now.AddSeconds(120);
            _count = 0;
            _window++;
            _actors.Clear();
            _flows.Clear();
        }
    }

    public string? Format(RoPacketLayoutProbe probe, RoPacketSource source, uint selfAid,
        string identitySource, DateTime now)
    {
        lock (_sync)
        {
            if (!source.IsActive || now >= _until || _count >= 256) return null;
            _count++;
            string Actor(uint aid)
            {
                if (aid == 0) return "zero";
                if (!_actors.TryGetValue(aid, out var label))
                {
                    if (_actors.Count >= 512) return "overflow";
                    _actors[aid] = label = _actors.Count + 1;
                }
                return $"a{label}";
            }
            if (!_flows.TryGetValue(source, out var flow)) _flows[source] = flow = _flows.Count + 1;
            return $"PACKET LAYOUT window={_window}; sample={_count}; flow=f{flow}; " +
                $"offset={probe.Offset}; boundary={probe.Boundary}; opcode=0x{probe.Opcode:X4}; " +
                $"actor={Actor(probe.Actor)}; self={Actor(selfAid)}; alternate={Actor(probe.AlternateActor)}; " +
                $"identity={identitySource}; {probe.Fields}";
        }
    }
}
