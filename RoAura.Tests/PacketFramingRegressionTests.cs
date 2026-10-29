using System.Buffers.Binary;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class PacketFramingRegressionTests
{
    [Theory]
    [InlineData((ushort)0x011A, 0u)]
    [InlineData((ushort)0x011A, 65535u)] // Hiding sends heal=-1
    [InlineData((ushort)0x09CB, uint.MaxValue)]
    [InlineData((ushort)0x09CB, 9000u)] // Heal amount is not skill level
    public void Nondamage_amount_is_not_limited_to_skill_levels(ushort opcode, uint amount)
    {
        var bytes = NonDamage(opcode, amount, 1);
        for (var split = 1; split < bytes.Length; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var skills = new List<RoSkillPacket>();
            scanner.SkillUsed += skills.Add;
            scanner.Process(bytes.AsSpan(0, split));
            Assert.Empty(skills);
            scanner.Process(bytes.AsSpan(split));
            var skill = Assert.Single(skills);
            Assert.Equal((ushort)51, skill.SkillId);
            Assert.Equal(opcode, skill.PacketId);
        }
    }

    [Theory]
    [InlineData((ushort)0x011A)]
    [InlineData((ushort)0x09CB)]
    public void Failed_nondamaging_cast_does_not_learn_or_trigger(ushort opcode)
    {
        var scanner = new RoPacketStreamScanner();
        var skills = new List<RoSkillPacket>();
        scanner.SkillUsed += skills.Add;
        scanner.Process(NonDamage(opcode, 1, 0));
        Assert.Empty(skills);
        scanner.Process(NonDamage(opcode, 1, 1));
        Assert.Single(skills);
    }

    [Theory]
    [InlineData((ushort)0x0119)]
    [InlineData((ushort)0x0229)]
    public void Karma_byte_does_not_reject_Hiding(ushort opcode)
    {
        var bytes = new byte[opcode == 0x0229 ? 15 : 13];
        W16(bytes, 0, opcode); W32(bytes, 2, 4200); W16(bytes, 10, 2);
        bytes[^1] = 255;
        var scanner = new RoPacketStreamScanner();
        var options = new List<RoOptionPacket>();
        scanner.OptionChanged += options.Add;
        scanner.Process(bytes);
        Assert.True(Assert.Single(options).IsHidden);
    }

    [Theory]
    [InlineData((ushort)0x0984)]
    [InlineData((ushort)0x08FF)]
    public void Status_on_enter_has_actor_before_status_id(ushort opcode)
    {
        var bytes = new byte[opcode == 0x0984 ? 28 : 24];
        W16(bytes, 0, opcode); W32(bytes, 2, 4200); W16(bytes, 6, 34);
        W32(bytes, 8, 60000);
        if (opcode == 0x0984) W32(bytes, 12, 12000);
        var scanner = new RoPacketStreamScanner();
        var statuses = new List<RoStatusPacket>();
        scanner.StatusChanged += statuses.Add;
        scanner.Process(bytes);
        var status = Assert.Single(statuses);
        Assert.Equal(4200u, status.Aid);
        Assert.Equal((ushort)34, status.StatusId);
        Assert.Equal(opcode == 0x0984 ? 12000u : 60000u, status.RemainingMs);
    }

    [Fact]
    public void Fragmented_chat_body_cannot_supply_status_or_self_identity()
    {
        var chat = new byte[50];
        W16(chat, 0, 0x009A); W16(chat, 2, (ushort)chat.Length);
        var fake = Status();
        fake.CopyTo(chat, 8);
        new byte[] { 0x83, 0x02, 0x68, 0x10, 0, 0 }.CopyTo(chat, 40);
        for (var split = 1; split < chat.Length; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var statuses = new List<RoStatusPacket>();
            var identities = new List<RoSelfAidPacket>();
            scanner.StatusChanged += statuses.Add;
            scanner.SelfAidObserved += identities.Add;
            scanner.Process(chat.AsSpan(0, split));
            scanner.Process(chat.AsSpan(split));
            Assert.Empty(statuses);
            Assert.Empty(identities);
            scanner.Process(Status());
            Assert.Single(statuses);
        }
    }

    [Fact]
    public void Consumed_frame_is_not_rescanned_when_carry_would_cut_into_its_body()
    {
        var scanner = new RoPacketStreamScanner();
        var statuses = new List<RoStatusPacket>();
        scanner.StatusChanged += statuses.Add;
        var chat = new byte[120];
        W16(chat, 0, 0x009A); W16(chat, 2, 120);
        Status().CopyTo(chat, 20);
        scanner.Process(chat);
        // The old 64-KiB rolling carry eventually dropped the chat header
        // while retaining its fake status; no consumed bytes should survive.
        scanner.Process(new byte[65500]);
        scanner.Process(Status());
        Assert.Single(statuses);
    }

    [Fact]
    public void Coalesced_self_identity_is_accepted_at_known_protocol_boundary()
    {
        var scanner = new RoPacketStreamScanner();
        var identities = new List<RoSelfAidPacket>();
        scanner.SelfAidObserved += identities.Add;
        var bytes = Status().Concat(new byte[] { 0x83, 0x02, 0x68, 0x10, 0, 0 }).ToArray();
        scanner.Process(bytes.AsSpan(0, bytes.Length - 1));
        Assert.Empty(identities);
        scanner.Process(bytes.AsSpan(bytes.Length - 1));
        Assert.Equal(4200u, Assert.Single(identities).Aid);
    }

    [Fact]
    public void Ground_cast_with_zero_target_and_new_ack_layout_are_supported()
    {
        var bytes = new byte[29];
        W16(bytes, 0, 0x0B1A); W32(bytes, 2, 4200); W16(bytes, 14, 89);
        var scanner = new RoPacketStreamScanner();
        var skills = new List<RoSkillPacket>();
        scanner.SkillUsed += skills.Add;
        scanner.Process(bytes);
        Assert.Equal(0u, Assert.Single(skills).TargetAid);
        var ground = new byte[18];
        W16(ground, 0, 0x0117); W16(ground, 2, 89); W32(ground, 4, 4200);
        scanner.Process(ground);
        Assert.Equal(2, skills.Count);
        Assert.Equal((ushort)0x0117, skills[1].PacketId);
    }

    [Fact]
    public void Mixed_equipment_loot_exp_zeny_and_status_stream_survives_every_split()
    {
        var inventory = new byte[73];
        W16(inventory, 0, 0x0B39); W16(inventory, 2, 73);
        W16(inventory, 5, 2); W32(inventory, 7, 1101);
        W32(inventory, 12, 2); W32(inventory, 16, 2); inventory[70] = 7;
        var unequip = new byte[9];
        W16(unequip, 0, 0x099A); W16(unequip, 2, 2); W32(unequip, 4, 2);
        var loot = new byte[70];
        W16(loot, 0, 0x0B41); W16(loot, 2, 3); W16(loot, 4, 5); W32(loot, 6, 501);
        var exp = new byte[18];
        W16(exp, 0, 0x0ACC); W32(exp, 2, 4200); W32(exp, 6, 900); W16(exp, 14, 1);
        var zeny = new byte[8];
        W16(zeny, 0, 0x00B1); W16(zeny, 2, 20); W32(zeny, 4, 12345);
        var stream = inventory.Concat(unequip).Concat(loot).Concat(exp).Concat(zeny).Concat(Status()).ToArray();
        for (var split = 1; split < stream.Length; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var gear = new List<RoEquipmentPacket>();
            var items = new List<RoItemAddedPacket>();
            var experiences = new List<RoExperiencePacket>();
            var balances = new List<RoZenyPacket>();
            var statuses = new List<RoStatusPacket>();
            scanner.EquipmentChanged += gear.Add; scanner.ItemAdded += items.Add;
            scanner.ExperienceChanged += experiences.Add; scanner.ZenyChanged += balances.Add;
            scanner.StatusChanged += statuses.Add;
            scanner.Process(stream.AsSpan(0, split)); scanner.Process(stream.AsSpan(split));
            Assert.Equal(2, gear.Count);
            Assert.Equal(1101, gear[1].ItemId);
            Assert.Equal((byte)7, gear[1].RefineLevel);
            Assert.False(gear[1].Equipped);
            Assert.Equal(5, Assert.Single(items).Count);
            Assert.Equal(900, Assert.Single(experiences).Amount);
            Assert.Equal(12345, Assert.Single(balances).Total);
            Assert.Single(statuses);
        }
    }

    [Fact]
    public void Legacy_equipment_ack_with_view_id_keeps_its_nine_byte_boundary()
    {
        var bytes = new byte[] { 0xAA, 0, 2, 0, 2, 0, 5, 0, 1 };
        for (var split = 1; split < bytes.Length; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var gear = new List<RoEquipmentPacket>();
            scanner.EquipmentChanged += gear.Add;
            scanner.Process(bytes.AsSpan(0, split));
            scanner.Process(bytes.AsSpan(split));
            Assert.True(Assert.Single(gear).Equipped);
        }
    }

    private static byte[] NonDamage(ushort opcode, uint amount, byte success)
    {
        var modern = opcode == 0x09CB;
        var bytes = new byte[modern ? 17 : 15];
        W16(bytes, 0, opcode); W16(bytes, 2, 51);
        if (modern) W32(bytes, 4, amount); else W16(bytes, 4, (ushort)amount);
        W32(bytes, modern ? 8 : 6, 4200); W32(bytes, modern ? 12 : 10, 4200);
        bytes[^1] = success;
        return bytes;
    }

    private static byte[] Status()
    {
        var bytes = new byte[29];
        W16(bytes, 0, 0x0983); W16(bytes, 2, 34); W32(bytes, 4, 4200);
        bytes[8] = 1; W32(bytes, 9, 60000); W32(bytes, 13, 17000);
        return bytes;
    }
    private static void W16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), v);
    private static void W32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
}
