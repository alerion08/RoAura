using System.Buffers.Binary;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class PacketFramingRegressionTests
{
    [Theory]
    [InlineData((ushort)11, 230u)]
    [InlineData((ushort)55, 45u)]
    public void Fragmented_level_update_keeps_the_following_experience_packet(ushort parameter, uint level)
    {
        var bytes = new byte[20];
        W16(bytes, 0, 0x00B0); W16(bytes, 2, parameter); W32(bytes, 4, level);
        W16(bytes, 8, 0x0ACB); W16(bytes, 10, 22); W32(bytes, 12, 1_000_000);

        for (var split = 1; split < 8; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var progress = new List<RoProgressPacket>();
            scanner.ProgressChanged += progress.Add;
            scanner.Process(bytes.AsSpan(0, split));
            Assert.Empty(progress);
            scanner.Process(bytes.AsSpan(split));
            Assert.Equal(new RoProgressPacket(parameter, level), progress[0]);
            Assert.Equal(new RoProgressPacket(22, 1_000_000), progress[1]);
            Assert.Equal(2, progress.Count);
        }
    }

    [Theory]
    [InlineData(0x0B72, 175, 92, 24, 4)]
    [InlineData(0x099D, 155, 72, 24, 4)]
    [InlineData(0x006B, 147, 64, 16, 27)]
    public void Fragmented_character_roster_reports_levels_by_cid(int opcode, int size,
        int baseOffset, int jobOffset, int header)
    {
        var bytes = new byte[header + 2 * size];
        W16(bytes, 0, (ushort)opcode);
        W16(bytes, 2, (ushort)bytes.Length);
        W32(bytes, header, 120_001);
        W16(bytes, header + baseOffset, 230);
        W32(bytes, header + jobOffset, 45);
        W32(bytes, header + size, 120_002);
        W16(bytes, header + size + baseOffset, 101);
        W32(bytes, header + size + jobOffset, 9);

        for (var split = 1; split < bytes.Length; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var rosters = new List<CharacterRosterLevel[]>();
            scanner.CharacterRosterObserved += rosters.Add;
            scanner.Process(bytes.AsSpan(0, split));
            Assert.Empty(rosters);
            scanner.Process(bytes.AsSpan(split));
            Assert.Equal(new[] { new CharacterRosterLevel(120_001, 230, 45),
                new CharacterRosterLevel(120_002, 101, 9) }, Assert.Single(rosters));
        }
        W32(bytes, header + size + jobOffset, 0);
        Assert.Empty(CharacterRosterLevels.Read(bytes));
    }

    [Fact]
    public void Level_update_after_unknown_bytes_requires_a_parameter_frame_and_keeps_legacy_progress()
    {
        var bytes = new byte[19];
        bytes.AsSpan(0, 3).Fill(0xEE);
        W16(bytes, 3, 0x00B0); W16(bytes, 5, 11); W32(bytes, 7, 230);
        W16(bytes, 11, 0x00B1); W16(bytes, 13, 22); W32(bytes, 15, 1_000_000);
        for (var split = 1; split < bytes.Length; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var progress = new List<RoProgressPacket>();
            scanner.ProgressChanged += progress.Add;
            scanner.Process(bytes.AsSpan(0, split));
            scanner.Process(bytes.AsSpan(split));
            Assert.Equal(new[] { new RoProgressPacket(11, 230),
                new RoProgressPacket(22, 1_000_000) }, progress);
        }
        bytes.AsSpan(11).Fill(0xEE);
        var unframed = new RoPacketStreamScanner();
        unframed.ProgressChanged += _ => throw new InvalidOperationException("False level frame");
        unframed.Process(bytes);
    }

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

    [Theory]
    [InlineData((ushort)1, 123456)]
    [InlineData((ushort)2, -500)]
    public void Legacy_experience_keeps_signed_amount_and_following_packet_boundary(ushort kind, int amount)
    {
        var bytes = new byte[14];
        W16(bytes, 0, 0x07F6); W32(bytes, 2, 4200);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6), amount);
        W16(bytes, 10, kind);
        var stream = bytes.Concat(Status()).ToArray();
        for (var split = 1; split < bytes.Length; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var experience = new List<RoExperiencePacket>();
            var statuses = new List<RoStatusPacket>();
            scanner.ExperienceChanged += experience.Add;
            scanner.StatusChanged += statuses.Add;
            scanner.Process(stream.AsSpan(0, split));
            Assert.Empty(experience);
            scanner.Process(stream.AsSpan(split));
            Assert.Equal(new RoExperiencePacket(4200, amount, kind, 0), Assert.Single(experience));
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
