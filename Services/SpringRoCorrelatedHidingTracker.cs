namespace RoAura.Services;

/// <summary>
/// SpringRO DEV only. Accepts a Hiding transition only when the candidate
/// status packet is directly followed by this client's successful Skill 51
/// acknowledgement (with the known optional 0x0229 between end and ack).
/// Raw OPTION_HIDE bit is deliberately not consulted: it is unreliable on
/// SpringRO even when the own actor ID is verified.
/// No status ID or actor field is decoded from the scrambled status payload.
/// </summary>
public sealed class SpringRoCorrelatedHidingTracker
{
    private RoPacketLayoutProbe? _pendingStatus;
    private long _expectedOffset;

    public void Reset()
    {
        _pendingStatus = null;
        _expectedOffset = 0;
    }

    public bool? Observe(RoPacketLayoutProbe probe, uint verifiedSelfAid)
    {
        if (!probe.Boundary || verifiedSelfAid == 0)
        {
            Reset();
            return null;
        }

        if (probe.Opcode is 0x0983 or 0x0196)
        {
            _pendingStatus = ValidStatus(probe, verifiedSelfAid) ? probe : null;
            _expectedOffset = probe.Offset + (probe.Opcode == 0x0983 ? 29 : 9);
            return null;
        }

        // Confirmed end on this server: 0x0196, own 0x0229, own Skill 51.
        // Accept the option frame's actor identity, never its hidden bit.
        if (probe.Opcode == 0x0229)
        {
            if (_pendingStatus?.Opcode == 0x0196 &&
                probe.Offset == _expectedOffset && probe.Actor == verifiedSelfAid)
            {
                _expectedOffset += 15;
                return null;
            }

            Reset();
            return null;
        }

        var pending = _pendingStatus;
        var expectedOffset = _expectedOffset;
        Reset();
        if (pending is null || probe.Opcode != 0x09CB ||
            probe.Offset != expectedOffset ||
            probe.SkillId != 51 || !probe.SkillSucceeded || probe.Actor != verifiedSelfAid)
            return null;

        return pending.Opcode == 0x0983;
    }

    private static bool ValidStatus(RoPacketLayoutProbe probe, uint ownAid)
    {
        if (probe.CandidateWords is not { Length: >= 8 } words ||
            words[6] != (ushort)(ownAid >> 16))
            return false;

        // Both packet families have state at byte +8. 0x0196 is only
        // nine bytes long, so byte +8 has no corresponding U16 sample.
        if (probe.StatusState != (probe.Opcode == 0x0983 ? 1 : 0))
            return false;

        // Repeated SpringRO Hiding starts carry 240000ms for both total
        // and remaining. A duration alone must never activate Hiding:
        // the following confirmed own Skill 51 is also mandatory.
        return probe.Opcode == 0x0196 ||
            (probe.StatusTotalMs == 240000 && probe.StatusLeftMs == 240000);
    }
}
