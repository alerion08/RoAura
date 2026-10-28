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
    public void Modern_non_damage_skill_09cb_is_recognized()
    {
        var bytes = new byte[17];
        Write16(bytes, 0, 0x09cb);
        Write16(bytes, 2, 54);
        Write32(bytes, 4, 10);
        Write32(bytes, 8, 4200);
        Write32(bytes, 12, 4200);
        bytes[16] = 1;
        var scanner = new RoPacketStreamScanner();
        var observed = new List<RoSkillPacket>();
        scanner.SkillUsed += observed.Add;
        scanner.Process(bytes);
        var found = Assert.Single(observed);
        Assert.Equal((ushort)54, found.SkillId);
        Assert.Equal((uint)4200, found.SourceAid);
    }

    [Fact]
    public void Legacy_cast_ack_013e_is_recognized()
    {
        var bytes = new byte[24];
        Write16(bytes, 0, 0x013e);
        Write32(bytes, 2, 4200);
        Write32(bytes, 6, 5550);
        Write16(bytes, 10, 80);
        Write16(bytes, 12, 110);
        Write16(bytes, 14, 23);
        Write32(bytes, 20, 500);
        var scanner = new RoPacketStreamScanner();
        var observed = new List<RoSkillPacket>();
        scanner.SkillUsed += observed.Add;
        scanner.Process(bytes);
        var found = Assert.Single(observed);
        Assert.Equal((ushort)23, found.SkillId);
        Assert.Equal((uint)4200, found.SourceAid);
    }

    [Fact]
    public void Legacy_damage_cast_0114_is_recognized()
    {
        var bytes = new byte[31];
        Write16(bytes, 0, 0x0114);
        Write16(bytes, 2, 69);
        Write32(bytes, 4, 4200);
        Write32(bytes, 8, 5000);
        Write16(bytes, 26, 3);
        var scanner = new RoPacketStreamScanner();
        var observed = new List<RoSkillPacket>();
        scanner.SkillUsed += observed.Add;
        scanner.Process(bytes);
        Assert.Equal((ushort)69, Assert.Single(observed).SkillId);
    }

    [Theory]
    [InlineData((ushort)0x0119)]
    [InlineData((ushort)0x0229)]
    public void Option_packets_report_Hiding_activation_and_removal(ushort opcode)
    {
        byte[] Create(uint mask)
        {
            var bytes = new byte[opcode == 0x0229 ? 15 : 13];
            Write16(bytes, 0, opcode);
            Write32(bytes, 2, 4200);
            if (opcode == 0x0229) Write32(bytes, 10, mask);
            else Write16(bytes, 10, (ushort)mask);
            bytes[^1] = 0;
            return bytes;
        }
        var scanner = new RoPacketStreamScanner();
        var observed = new List<RoOptionPacket>();
        scanner.OptionChanged += observed.Add;
        scanner.Process(Create(0));
        scanner.Process(Create(2));
        scanner.Process(Create(2));
        scanner.Process(Create(0));
        Assert.Equal(4, observed.Count);
        Assert.Equal((uint)4200, observed[0].Aid);
        Assert.False(observed[0].IsHidden);
        Assert.True(observed[1].IsHidden);
        Assert.True(observed[2].IsHidden);
        Assert.False(observed[3].IsHidden);
    }

    [Theory]
    [InlineData((ushort)0x0119)]
    [InlineData((ushort)0x0229)]
    public void Full_16_bit_actor_flags_are_valid_not_capped_at_4096(ushort opcode)
    {
        var bytes = new byte[opcode == 0x0119 ? 13 : 15];
        Write16(bytes, 0, opcode);
        Write32(bytes, 2, 2004474);
        Write16(bytes, 6, 0x8000); // Highest bodyState bit is legal
        Write16(bytes, 8, 0x4000); // High healthState bit is legal
        if (opcode == 0x0119) Write16(bytes, 10, 2);
        else Write32(bytes, 10, 2);
        bytes[^1] = 0;
        var scanner = new RoPacketStreamScanner();
        var received = new List<RoOptionPacket>();
        scanner.OptionChanged += received.Add;
        scanner.Process(bytes);
        Assert.True(Assert.Single(received).IsHidden);
    }

    [Fact]
    public void Malformed_actor_option_must_not_trigger_statuses()
    {
        var bytes = new byte[15];
        Write16(bytes, 0, 0x0229);
        Write32(bytes, 2, 4200);
        Write32(bytes, 10, 2);
        bytes[14] = 4; // Invalid PK-mode boolean
        var scanner = new RoPacketStreamScanner();
        var count = 0;
        scanner.OptionChanged += _ => count++;
        scanner.Process(bytes);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Invalid_option_candidate_produces_metadata_diagnostic_but_no_Hiding()
    {
        var bytes = new byte[15];
        Write16(bytes, 0, 0x0229);
        Write32(bytes, 2, 4200);
        Write32(bytes, 10, 2);
        bytes[14] = 7; // invalid PK flag
        var scanner = new RoPacketStreamScanner();
        var rejected = new List<ushort>();
        var reasons = new List<string>();
        var accepted = 0;
        scanner.OptionCandidateRejected += rejected.Add;
        scanner.OptionCandidateRejectedDetailed += (opcode, reason) => reasons.Add(reason);
        scanner.OptionChanged += _ => accepted++;
        scanner.Process(bytes);
        Assert.Equal(0, accepted);
        Assert.Contains((ushort)0x0229, rejected);
        Assert.Contains("pk-mode-not-boolean-or-embedded-signature", reasons);
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
