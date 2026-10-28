namespace RoAura.Services;

/// <summary>
/// DEV-only, SpringRO-only investigation of the reproducible
/// status -> optional option -> successful own skill packet sequence.
/// Never manufactures a Status ID or modifies an overlay state.
/// No packet dumps, actor numbers, or raw candidate scalar values are logged.
/// </summary>
public sealed class SpringRoStatusSkillCorrelationDiagnostics
{
    private sealed class Series
    {
        public int Count;
        public readonly Dictionary<int, ushort> FirstWords = [];
        public readonly HashSet<int> StableWordOffsets = [];
        public ushort FirstXorDerivedStatus;
        public ushort FirstAddDerivedStatus;
        public bool XorDerivedStable = true;
        public bool AddDerivedStable = true;
        public bool ActorHighHalfMatchesAll = true;
    }

    private readonly Dictionary<(ushort SkillId, ushort StatusOpcode), Series> _series = [];
    private RoPacketLayoutProbe? _pendingStatus;
    private long _expectedOffset;

    public void Reset()
    {
        _series.Clear();
        _pendingStatus = null;
        _expectedOffset = 0;
    }

    public string? Observe(RoPacketLayoutProbe probe, uint verifiedSelfAid)
    {
        if (verifiedSelfAid == 0 || !probe.Boundary)
        {
            _pendingStatus = null;
            return null;
        }

        if (probe.Opcode is 0x0983 or 0x0196)
        {
            _pendingStatus = probe;
            _expectedOffset = probe.Offset + (probe.Opcode == 0x0983 ? 29 : 9);
            return null;
        }

        // On SpringRO an ending 0x0196 is followed by an own 0x0229
        // before 0x09CB, whereas 0x0983 can directly precede 0x09CB.
        if (probe.Opcode == 0x0229)
        {
            if (_pendingStatus is not null && probe.Offset == _expectedOffset &&
                probe.Actor == verifiedSelfAid)
            {
                _expectedOffset += 15;
                return null;
            }

            _pendingStatus = null;
            return null;
        }

        var status = _pendingStatus;
        _pendingStatus = null;
        if (probe.Opcode != 0x09CB || status is null || probe.Offset != _expectedOffset ||
            !probe.SkillSucceeded || probe.Actor != verifiedSelfAid ||
            probe.SkillId is not (51 or 138) ||
            (probe.SkillId == 138 && status.Opcode != 0x0983) ||
            status.CandidateWords is not { } words ||
            status.CandidateDwords is not { } dwords)
            return null;

        var key = (probe.SkillId, status.Opcode);
        if (!_series.TryGetValue(key, out var series))
            _series[key] = series = new Series();
        series.Count++;
        // Bounded per-flow output; comparisons continue silently after 16 samples.
        if (series.Count > 16) return null;

        // Diagnostic hypothesis only: if SpringRO uses one per-packet XOR mask
        // on status(+2) and actor-low(+4), undoing it with the confirmed own AID
        // yields a stable EFST across repeated casts. Addition is an independent
        // control hypothesis. Neither value is used to drive any live status.
        ushort selfLow = (ushort)verifiedSelfAid;
        ushort actorLow = words[4];
        ushort xorCandidate = (ushort)(words[2] ^ actorLow ^ selfLow);
        ushort addCandidate = unchecked((ushort)(words[2] - actorLow + selfLow));
        bool actorHighHalfMatches = words[6] == (ushort)(verifiedSelfAid >> 16);
        if (series.Count == 1)
        {
            series.FirstXorDerivedStatus = xorCandidate;
            series.FirstAddDerivedStatus = addCandidate;
        }
        else
        {
            series.XorDerivedStable &= xorCandidate == series.FirstXorDerivedStatus;
            series.AddDerivedStable &= addCandidate == series.FirstAddDerivedStatus;
        }
        series.ActorHighHalfMatchesAll &= actorHighHalfMatches;

        for (var offset = 2; offset < words.Length; offset++)
        {
            // Exclude known state/timer region (+8..+16) and opcode.
            // Candidate EFST / actor positions are unknown until field-tested.
            if (offset is >= 8 and <= 16 || offset > 27) continue;
            if (series.Count == 1)
            {
                series.FirstWords[offset] = words[offset];
                series.StableWordOffsets.Add(offset);
            }
            else if (series.FirstWords.GetValueOrDefault(offset) != words[offset])
                series.StableWordOffsets.Remove(offset);
        }

        var ownOffsets = new List<int>();
        for (var offset = 2; offset < dwords.Length; offset++)
            if (dwords[offset] == verifiedSelfAid) ownOffsets.Add(offset);

        var stable = series.Count < 3 ? "pending-3-samples" :
            (series.StableWordOffsets.Count == 0
                ? "none" : string.Join(",", series.StableWordOffsets.OrderBy(x => x)));
        var own = ownOffsets.Count == 0 ? "none" : string.Join(",", ownOffsets);
        var kind = status.Opcode == 0x0983 ? "start-candidate" : "end-candidate";
        var xorResult = series.Count < 3 ? "pending-3-samples"
            : series.XorDerivedStable ? $"stable-candidate-{series.FirstXorDerivedStatus}" : "unstable";
        var addResult = series.Count < 3 ? "pending-3-samples"
            : series.AddDerivedStable ? $"stable-candidate-{series.FirstAddDerivedStatus}" : "unstable";
        return $"SPRINGRO STATUS-SKILL PAIR: skill={probe.SkillId}; kind={kind}; " +
               $"statusOpcode=0x{status.Opcode:X4}; statusOffset={status.Offset}; " +
               $"skillOffset={probe.Offset}; sample={series.Count}; " +
               $"selfAidU32Offsets={own}; stableU16Offsets={stable}; " +
               $"actorHigh16MatchesSelf={actorHighHalfMatches}; actorHigh16AllMatch={series.ActorHighHalfMatchesAll}; " +
               $"xorDerivedStatus={xorResult}; addDerivedStatus={addResult}; " +
               $"statusFields={status.Fields}";
    }
}
