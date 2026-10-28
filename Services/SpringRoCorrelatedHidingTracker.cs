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
        if (probe.CandidateWords is not { Length: >= 9 } words ||
            words[6] != (ushort)(ownAid >> 16))
            return false;

        // Both packet families have state at byte +8. This check also filters
        // spurious signature hits during scanner re-synchronization.
        if ((byte)words[8] != (probe.Opcode == 0x0983 ? 1 : 0))
            return false;

        // In three independent SpringRO samples, Hiding's start candidate
        // carries 240000 ms for both total and remaining. Duration alone
        // must never activate Hiding; the confirmed own Skill 51 is required.
        return probe.Opcode == 0x0196 || probe.CandidateDwords is { Length: >= 17 } dwords &&
            dwords[9] == 240000 && dwords[13] == 240000;
    }
}
