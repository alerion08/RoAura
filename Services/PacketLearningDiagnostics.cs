namespace RoAura.Services;

/// <summary>Short metadata-only window shared by normal and development builds.</summary>
public sealed class PacketLearningDiagnostics
{
    private DateTime _until;
    private int _count;

    public void Begin(DateTime now) { _until = now.AddSeconds(20); _count = 0; }

    public string? Format(DateTime now, string kind, ushort opcode, int id, bool armed,
        bool current, bool identityKnown, bool? own)
    {
        if (now >= _until || _count++ >= 64) return null;
        return $"PACKET LEARN {kind}; opcode=0x{opcode:X4}; id={id}; armed={armed}; " +
               $"valid={current}; identityKnown={identityKnown}; own={own?.ToString() ?? "not-required"}";
    }
}
