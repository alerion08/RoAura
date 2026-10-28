using System.Text.Json;

namespace RoAura.Services;

/// <summary>
/// Explicit opt-in evidence export, never a decoder. Keeps exact bytes so a
/// single field run can be replayed offline instead of testing one mask per build.
/// Only a short status/[option]/successful self-targeted skill chain is exported.
/// No login, chat, socket addresses, names or unrelated payloads are retained.
/// </summary>
public sealed class SpringRoStatusWireCapture
{
    public const string Argument = "--springro-status-samples";
    public const string Marker = "SPRINGRO WIRE SAMPLE ";
    private sealed record Pending(RoPacketLayoutProbe Status, uint Self, DateTime At,
        long NextOffset, RoPacketLayoutProbe? Option = null);
    private readonly object _sync = new();
    private readonly Dictionary<RoPacketSource, Pending> _pending = [];
    private DateTime _start;
    private DateTime _until;
    private int _count;
    private int _window;

    public void Begin(DateTime now)
    {
        lock (_sync)
        {
            _start = now;
            _until = now.AddSeconds(120);
            _count = 0;
            _window++;
            _pending.Clear();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _until = DateTime.MinValue;
            _pending.Clear();
        }
    }

    public string? Observe(RoPacketLayoutProbe probe, RoPacketSource source, uint self, DateTime now)
    {
        lock (_sync)
        {
            if (now < _start || now >= _until || _count >= 16 || !source.IsActive ||
                self == 0 || !probe.Boundary || probe.WireBytes.IsEmpty)
            {
                _pending.Remove(source);
                return null;
            }
            if (probe.Opcode is 0x0983 or 0x0196)
            {
                _pending.Remove(source);
                foreach (var stale in _pending.Where(p => !p.Key.IsActive ||
                             now - p.Value.At > TimeSpan.FromSeconds(2)).Select(p => p.Key).ToList())
                    _pending.Remove(stale);
                if (_pending.Count < 8 && probe.CandidateWords is { Length: >= 8 } words &&
                    words[6] == (ushort)(self >> 16) &&
                    probe.StatusState == (probe.Opcode == 0x0983 ? 1 : 0))
                    _pending[source] = new(probe, self, now, probe.Offset + probe.WireBytes.Length);
                return null;
            }
            if (!_pending.Remove(source, out var pending) || pending.Self != self ||
                now - pending.At > TimeSpan.FromSeconds(2) || probe.Offset != pending.NextOffset)
                return null;
            if (probe.Opcode == 0x0229 && pending.Option is null && probe.Actor == self &&
                pending.Status.Opcode == 0x0196)
            {
                _pending[source] = pending with
                { Option = probe, NextOffset = probe.Offset + probe.WireBytes.Length };
                return null;
            }
            if (probe.Opcode != 0x09CB || !probe.SkillSucceeded ||
                probe.Actor != self || probe.AlternateActor != self ||
                probe.SkillId is not (51 or 138) ||
                (probe.SkillId == 138 && pending.Status.Opcode != 0x0983))
                return null;
            _count++;
            return Marker + JsonSerializer.Serialize(new
            {
                schema = 1, window = _window, sample = _count, observedAtUtc = now,
                selfAid = self, skillId = probe.SkillId, statusOffset = pending.Status.Offset,
                skillOffset = probe.Offset,
                statusHex = Convert.ToHexString(pending.Status.WireBytes.Span),
                optionHex = pending.Option is { } option ? Convert.ToHexString(option.WireBytes.Span) : "",
                skillHex = Convert.ToHexString(probe.WireBytes.Span)
            });
        }
    }
}
