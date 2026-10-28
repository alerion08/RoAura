using System.Buffers.Binary;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class PacketSkillEffectRegressionTests
{
    [Fact]
    public void New_status_change_0983_reads_total_then_left_not_backwards()
    {
        var bytes = new byte[29];
        Write16(bytes, 0, 0x0983);
        Write16(bytes, 2, 812);
        Write32(bytes, 4, 4200);
        bytes[8] = 1;
        Write32(bytes, 9, 60000); // Total at +9
        Write32(bytes, 13, 17000); // Left at +13
        var scanner = new RoPacketStreamScanner();
        var observed = new List<RoStatusPacket>();
        scanner.StatusChanged += observed.Add;
        scanner.Process(bytes.AsSpan(0, 13));
        Assert.Empty(observed);
        scanner.Process(bytes.AsSpan(13));
        var result = Assert.Single(observed);
        Assert.Equal((ushort)0x0983, result.PacketId);
        Assert.Equal((ushort)812, result.StatusId);
        Assert.Equal((uint)4200, result.Aid);
        Assert.True(result.Active);
        Assert.Equal((uint)17000, result.RemainingMs);
        Assert.Equal((uint)60000, result.TotalMs);
        // Replaying the same exact bytes as a second packet is a new event;
        // the prior carry must not manufacture multiple events during reassembly.
    }

    [Fact]
    public void Legacy_043f_uses_remaining_as_total_when_no_total_exists()
    {
        var bytes = new byte[25];
        Write16(bytes, 0, 0x043f);
        Write16(bytes, 2, 923);
        Write32(bytes, 4, 4200);
        bytes[8] = 1;
        Write32(bytes, 9, 12000);
        var scanner = new RoPacketStreamScanner();
        RoStatusPacket? captured = null;
        scanner.StatusChanged += p => captured = p;
        scanner.Process(bytes);
        Assert.NotNull(captured);
        Assert.Equal((uint)12000, captured.Value.RemainingMs);
        Assert.Equal((uint)12000, captured.Value.TotalMs);
    }

    [Fact]
    public void Legacy_nondamaging_skill_cast_011a_can_be_learned()
    {
        var bytes = new byte[15];
        Write16(bytes, 0, 0x011a);
        Write16(bytes, 2, 34);
        Write16(bytes, 4, 5);
        Write32(bytes, 6, 4200);
        Write32(bytes, 10, 4200);
        bytes[14] = 1;
        var scanner = new RoPacketStreamScanner();
        var observed = new List<RoSkillPacket>();
        scanner.SkillUsed += observed.Add;
        scanner.Process(bytes);
        var found = Assert.Single(observed);
        Assert.Equal((ushort)34, found.SkillId);
        Assert.Equal((uint)4200, found.SourceAid);
    }

    [Fact]
    public void Damage_skill_cast_01de_can_be_learned()
    {
        var bytes = new byte[33];
        Write16(bytes, 0, 0x01de);
        Write16(bytes, 2, 81);
        Write32(bytes, 4, 4200);
        Write32(bytes, 8, 9900);
        Write16(bytes, 28, 7);
        Write16(bytes, 30, 2);
        var scanner = new RoPacketStreamScanner();
        var observed = new List<RoSkillPacket>();
        scanner.SkillUsed += observed.Add;
        scanner.Process(bytes);
        var found = Assert.Single(observed);
        Assert.Equal((ushort)81, found.SkillId);
        Assert.Equal((uint)4200, found.SourceAid);
        Assert.Equal((uint)9900, found.TargetAid);
    }

    [Fact]
    public void Invalid_skill_packets_are_rejected()
    {
        var bytes = new byte[15];
        Write16(bytes, 0, 0x011a);
        Write16(bytes, 2, 34);
        Write16(bytes, 4, 5);
        Write32(bytes, 6, 4200);
        Write32(bytes, 10, 4200);
        bytes[14] = 3; // Not a valid Boolean.
        var scanner = new RoPacketStreamScanner();
        var count = 0;
        scanner.SkillUsed += _ => count++;
        scanner.Process(bytes);
        Assert.Equal(0, count);
    }

    private static void Write16(byte[] b, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset, 2), value);
    private static void Write32(byte[] b, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset, 4), value);
}
