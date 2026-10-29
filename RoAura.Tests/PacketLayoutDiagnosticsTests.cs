using System.Buffers.Binary;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class PacketLayoutDiagnosticsTests
{
    private static byte[] Status(ushort status = 40000)
    {
        var bytes = new byte[29];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0x0983);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), status);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2004474);
        bytes[8] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(9), 240000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(13), 240000);
        return bytes;
    }

    [Fact]
    public void Rejected_status_fields_are_observed_once_across_every_fragment_boundary()
    {
        var bytes = Status();
        for (var split = 1; split < bytes.Length; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var probes = new List<RoPacketLayoutProbe>();
            var statuses = new List<RoStatusPacket>();
            scanner.LayoutProbed += probes.Add;
            scanner.StatusChanged += statuses.Add;
            scanner.Process(bytes.AsSpan(0, split));
            Assert.Empty(probes);
            scanner.Process(bytes.AsSpan(split));
            var probe = Assert.Single(probes);
            Assert.Equal(0, probe.Offset);
            Assert.Contains("status=40000", probe.Fields);
            Assert.Empty(statuses); // Instrumentation must never loosen semantic validation.
        }
    }

    [Fact]
    public void Probe_reports_option_mask_without_changing_Hiding_decode()
    {
        var bytes = new byte[15];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0x0229);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2), 2004474);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), 0xA1234562);
        bytes[14] = 255;
        var scanner = new RoPacketStreamScanner();
        RoPacketLayoutProbe? probe = null;
        RoOptionPacket option = default;
        scanner.LayoutProbed += p => probe = p;
        scanner.OptionChanged += p => option = p;
        scanner.Process(bytes);
        Assert.Contains("option=0xA1234562", probe!.Fields);
        Assert.Contains("karma=255", probe.Fields);
        Assert.True(option.IsHidden);
    }

    [Fact]
    public void Diagnostic_window_is_bounded_rearmable_and_does_not_expose_actor_ids()
    {
        var diagnostics = new PacketLayoutDiagnostics();
        var source = new RoPacketSource(0, new(1, 10, 2, 20));
        var now = DateTime.UtcNow;
        var probe = RoPacketLayoutProbe.Create(Status(), 0, true)!;
        Assert.Null(diagnostics.Format(probe, source, 2004474, "EXP", now));
        diagnostics.Begin(now);
        for (var i = 0; i < 256; i++)
        {
            var line = diagnostics.Format(probe, source, 2004474, "EXP", now)!;
            Assert.Contains("actor=a1; self=a1", line);
            Assert.DoesNotContain("2004474", line);
        }
        Assert.Null(diagnostics.Format(probe, source, 2004474, "EXP", now));
        diagnostics.Begin(now);
        Assert.NotNull(diagnostics.Format(probe, source, 2004474, "EXP", now));
        Assert.Null(diagnostics.Format(probe, source, 2004474, "EXP", now.AddSeconds(120)));
        diagnostics.Begin(now);
        source.Close();
        Assert.Null(diagnostics.Format(probe, source, 2004474, "EXP", now));
    }

    [Fact]
    public void Coalesced_candidates_keep_absolute_offsets_and_stream_reset_restarts_offset()
    {
        var scanner = new RoPacketStreamScanner();
        var probes = new List<RoPacketLayoutProbe>();
        scanner.LayoutProbed += probes.Add;
        scanner.Process(Status(34).Concat(Status(35)).ToArray());
        scanner.Process(Status(36));
        Assert.Equal(new long[] { 0, 29, 58 }, probes.Select(p => p.Offset));
        scanner.ResetStream();
        scanner.Process(Status(37));
        Assert.Equal(0, probes[^1].Offset);
    }

    [Fact]
    public void Chat_content_is_not_reported_as_packet_metadata()
    {
        var chat = new byte[33];
        BinaryPrimitives.WriteUInt16LittleEndian(chat, 0x008D);
        BinaryPrimitives.WriteUInt16LittleEndian(chat.AsSpan(2), 33);
        Status().CopyTo(chat, 4);
        var scanner = new RoPacketStreamScanner();
        scanner.LayoutProbed += _ => Assert.Fail("Opaque chat content must not be probed");
        scanner.Process(chat);
    }
}
