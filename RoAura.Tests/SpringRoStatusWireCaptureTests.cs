using System.Buffers.Binary;
using System.Text.Json;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class SpringRoStatusWireCaptureTests
{
    private const uint Self = 0x001E1234;
    private static readonly DateTime Now = new(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
    private static RoPacketSource Source(ushort port = 1) => new(1, new(1, port, 2, 57143));
    private static RoPacketLayoutProbe Status(long offset = 0, bool end = false, bool boundary = true)
    {
        var b = new byte[end ? 9 : 29];
        BinaryPrimitives.WriteUInt16LittleEndian(b, end ? (ushort)0x0196 : (ushort)0x0983);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), 47410);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), Self ^ 0xABCD); // synthetic unknown low field
        b[8] = end ? (byte)0 : (byte)1;
        if (!end)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(9), 180000);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(13), 180000);
        }
        return RoPacketLayoutProbe.Create(b, offset, boundary, true)!;
    }
    private static RoPacketLayoutProbe Skill(long offset = 29, uint actor = Self,
        uint target = Self, bool success = true, ushort skill = 138)
    {
        var b = new byte[17];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 0x09CB);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), skill);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), target);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), actor);
        b[16] = success ? (byte)1 : (byte)0;
        return RoPacketLayoutProbe.Create(b, offset, true, true)!;
    }

    [Fact]
    public void Exact_status_and_ack_are_recoverable_after_correlated_self_cast()
    {
        var capture = new SpringRoStatusWireCapture();
        var source = Source();
        var status = Status(); var skill = Skill();
        capture.Begin(Now);
        Assert.Null(capture.Observe(status, source, Self, Now));
        var line = capture.Observe(skill, source, Self, Now.AddMilliseconds(1))!;
        Assert.StartsWith(SpringRoStatusWireCapture.Marker, line);
        using var json = JsonDocument.Parse(line[SpringRoStatusWireCapture.Marker.Length..]);
        Assert.Equal(Self, json.RootElement.GetProperty("selfAid").GetUInt32());
        Assert.Equal(status.WireBytes.ToArray(), Convert.FromHexString(json.RootElement.GetProperty("statusHex").GetString()!));
        Assert.Equal(skill.WireBytes.ToArray(), Convert.FromHexString(json.RootElement.GetProperty("skillHex").GetString()!));
        Assert.Equal("", json.RootElement.GetProperty("optionHex").GetString());
        Assert.Null(capture.Observe(skill, source, Self, Now)); // no duplicate consumed pair
    }

    [Fact]
    public void Unarmed_expired_closed_reset_and_missing_wire_data_never_export()
    {
        var capture = new SpringRoStatusWireCapture(); var source = Source();
        capture.Observe(Status(), source, Self, Now);
        Assert.Null(capture.Observe(Skill(), source, Self, Now));
        capture.Begin(Now);
        capture.Observe(Status() with { WireBytes = default }, source, Self, Now);
        Assert.Null(capture.Observe(Skill(), source, Self, Now));
        capture.Observe(Status(), source, Self, Now);
        Assert.Null(capture.Observe(Skill(), source, Self, Now.AddSeconds(120)));
        capture.Begin(Now); capture.Observe(Status(), source, Self, Now); capture.Reset();
        Assert.Null(capture.Observe(Skill(), source, Self, Now));
        capture.Begin(Now); capture.Observe(Status(), source, Self, Now); source.Close();
        Assert.Null(capture.Observe(Skill(), source, Self, Now));
    }

    [Fact]
    public void Rejects_foreign_failed_other_target_changed_identity_gap_and_unaligned_pairs()
    {
        var capture = new SpringRoStatusWireCapture(); var source = Source();
        capture.Begin(Now);
        foreach (var bad in new[] { Skill(actor: Self + 1), Skill(target: Self + 1),
                     Skill(success: false), Skill(skill: 52), Skill(offset: 30) })
        {
            capture.Observe(Status(), source, Self, Now);
            Assert.Null(capture.Observe(bad, source, Self, Now));
        }
        capture.Observe(Status(boundary: false), source, Self, Now);
        Assert.Null(capture.Observe(Skill(), source, Self, Now));
        capture.Observe(Status(), source, Self, Now);
        Assert.Null(capture.Observe(Skill(), source, Self + 1, Now));
        capture.Observe(Status(), source, 0, Now);
        Assert.Null(capture.Observe(Skill(), source, Self, Now));
        capture.Observe(Status(), source, Self, Now);
        Assert.Null(capture.Observe(Skill(), source, Self, Now.AddSeconds(3)));
    }

    [Fact]
    public void Lifetimes_do_not_mix_and_limit_is_global_per_learning_window()
    {
        var capture = new SpringRoStatusWireCapture(); var a = Source(); var b = Source(2);
        capture.Begin(Now);
        capture.Observe(Status(), a, Self, Now);
        Assert.Null(capture.Observe(Skill(), b, Self, Now));
        for (int i = 0; i < 16; i++)
        {
            capture.Observe(Status(i * 100), a, Self, Now);
            Assert.NotNull(capture.Observe(Skill(i * 100 + 29), a, Self, Now));
        }
        capture.Observe(Status(), b, Self, Now);
        Assert.Null(capture.Observe(Skill(), b, Self, Now));
        capture.Begin(Now);
        capture.Observe(Status(), b, Self, Now);
        Assert.NotNull(capture.Observe(Skill(), b, Self, Now));
    }

    [Fact]
    public void Hiding_end_exports_the_intermediate_own_option_frame_without_using_its_hide_bit()
    {
        var capture = new SpringRoStatusWireCapture(); var source = Source();
        var bytes = new byte[15];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0x0229);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2), Self);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), 0xAABBCC02);
        var option = RoPacketLayoutProbe.Create(bytes, 9, true, true)!;
        capture.Begin(Now);
        capture.Observe(Status(end: true), source, Self, Now);
        Assert.Null(capture.Observe(option, source, Self, Now));
        var line = capture.Observe(Skill(24, skill: 51), source, Self, Now)!;
        using var json = JsonDocument.Parse(line[SpringRoStatusWireCapture.Marker.Length..]);
        Assert.Equal(Convert.ToHexString(bytes), json.RootElement.GetProperty("optionHex").GetString());
    }

    [Fact]
    public void Actual_scanner_exports_fragmented_rejected_status_followed_by_own_ack()
    {
        var wire = Status().WireBytes.ToArray().Concat(Skill().WireBytes.ToArray()).ToArray();
        for (var split = 1; split < wire.Length; split++)
        {
            var capture = new SpringRoStatusWireCapture(); var source = Source();
            capture.Begin(Now);
            var scanner = new RoPacketStreamScanner { CaptureStatusWireBytes = true };
            var samples = new List<string>();
            scanner.LayoutProbed += p =>
            {
                if (capture.Observe(p, source, Self, Now) is { } sample) samples.Add(sample);
            };
            var statuses = new List<RoStatusPacket>();
            scanner.StatusChanged += statuses.Add;
            scanner.Process(wire.AsSpan(0, split));
            scanner.Process(wire.AsSpan(split));
            Assert.Single(samples);
            Assert.Empty(statuses); // A sample is not permission to learn the scrambled status.
        }
    }

    [Fact]
    public void Default_probes_do_not_retain_wire_bytes_and_opt_in_copies_the_input()
    {
        var bytes = Status().WireBytes.ToArray();
        Assert.True(RoPacketLayoutProbe.Create(bytes, 0, true)!.WireBytes.IsEmpty);
        var copy = RoPacketLayoutProbe.Create(bytes, 0, true, true)!;
        bytes[2] ^= 255;
        Assert.NotEqual(bytes[2], copy.WireBytes.Span[2]);
    }
}
