using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class MuhRoPacketSupportTests
{
    private static CharacterSessionFlow Flow(ushort port) => new(1, port, 2, 54729);
    private static RoPacketStreamScanner Scanner(NpcapPacketCapture capture, ushort port = 50001) =>
        (RoPacketStreamScanner)typeof(NpcapPacketCapture).GetMethod("CreateScanner",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(capture, [0, Flow(port)])!;

    [Theory]
    [InlineData("MuhRO")]
    [InlineData("muhro")]
    [InlineData("Muh")]
    public void Profile_survives_settings_and_adds_client_without_removing_custom_names(string profile)
    {
        var settings = new AppSettings { ServerProfile = profile, AllowedProcesses = ["Custom.exe"] };
        GameServerProfile.EnsureClientProcess(settings);
        GameServerProfile.EnsureClientProcess(settings);
        Assert.Equal(new[] { "Custom.exe", "Muh" }, settings.AllowedProcesses);
        Assert.Equal(GameServerProfile.MuhRo, JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.ServerProfile);
        Assert.False(GameServerProfile.UsesSpringRoClientStatus(profile));
        Assert.True(GameServerProfile.UsesSpringRoClientStatus("SpringRO"));
        foreach (var feature in new[] { AppFeature.Cooldowns, AppFeature.Effects, AppFeature.Reminders, AppFeature.Equipment })
        {
            Assert.True(FeatureAvailability.CanUse(profile, true, feature));
            Assert.False(FeatureAvailability.CanUse(profile, false, feature));
        }
        Assert.False(GameServerProfile.SupportsServerDatabases(profile));
        Assert.False(GameServerProfile.SupportsMarket(profile));
        Assert.NotEqual(CharacterStatisticsKey.Create("MuhRO", 123), CharacterStatisticsKey.Create("SpringRO", 123));
    }

    [Fact]
    public void Existing_exe_process_alias_is_not_duplicated()
    {
        var settings = new AppSettings { ServerProfile = "MuhRO", AllowedProcesses = ["muh.exe"] };
        GameServerProfile.EnsureClientProcess(settings);
        Assert.Single(settings.AllowedProcesses);
    }

    [Fact]
    public void Direct_status_start_refresh_and_removal_reach_timeline_with_server_duration()
    {
        using var capture = new NpcapPacketCapture();
        var scanner = Scanner(capture);
        var packets = new List<RoPacketObservation<RoStatusPacket>>();
        capture.StatusChanged += packets.Add;
        scanner.Process(new byte[] { 0x83, 2, 0x68, 0x10, 0, 0 }); // synthetic own AID 4200
        var effects = new[] { new TrackedEffect { StatusId = 6, Enabled = true } };
        var reminders = new[] { new ReminderItem { StatusId = 6 } };
        var timeline = new StatusTrackingController();
        scanner.Process(Status(6, 4200, true));
        var start = Assert.Single(packets);
        Assert.True(start.IsOwnActor(4200));
        Assert.True(capture.IsCurrentObservation(start));
        Assert.Equal(165000u, start.Packet.RemainingMs);
        timeline.Observe(start.Packet, start.ObservedAtUtc);
        timeline.Synchronize(effects, reminders, start.ObservedAtUtc.AddSeconds(5), "ru");
        Assert.Equal(160, effects[0].RemainingSeconds);
        Assert.True(reminders[0].IsStatusPresent);
        scanner.Process(Status(6, 4200, true));
        Assert.Equal(2, packets.Count);
        scanner.Process(Status(6, 4200, false));
        var removed = packets[^1];
        timeline.Observe(removed.Packet, start.ObservedAtUtc.AddSeconds(10));
        timeline.Synchronize(effects, reminders, start.ObservedAtUtc.AddSeconds(10), "en");
        Assert.False(effects[0].IsActive);
        Assert.False(reminders[0].IsStatusPresent);
        scanner.Process(Status(5, 9000, true));
        Assert.False(packets[^1].IsOwnActor(9000));
    }

    [Fact]
    public void Skill_and_actorless_cooldown_keep_identity_and_failed_cast_does_not_trigger()
    {
        using var capture = new NpcapPacketCapture();
        var scanner = Scanner(capture);
        var skills = new List<RoPacketObservation<RoSkillPacket>>();
        var delays = new List<RoPacketObservation<RoSkillCooldownPacket>>();
        capture.SkillUsed += skills.Add; capture.SkillCooldown += delays.Add;
        scanner.Process(new byte[] { 0x83, 2, 0x68, 0x10, 0, 0 });
        var cast = new byte[17]; W16(cast, 0, 0x09CB); W16(cast, 2, 138);
        W32(cast, 8, 4200); W32(cast, 12, 4200); cast[16] = 1;
        scanner.Process(cast);
        Assert.True(Assert.Single(skills).IsOwnActor(4200));
        Assert.Equal((ushort)138, skills[0].Packet.SkillId);
        cast[16] = 0; scanner.Process(cast); Assert.Single(skills);
        var cooldown = new byte[8]; W16(cooldown, 0, 0x043D); W16(cooldown, 2, 5288); W32(cooldown, 4, 3000);
        scanner.Process(cooldown);
        Assert.Equal((ushort)5288, Assert.Single(delays).Packet.SkillId);
        Assert.Equal(3000u, delays[0].Packet.DelayMs);
        Assert.True(capture.IsCurrentObservation(delays[0]));
    }

    [Fact]
    public void Missing_inventory_reports_diagnostic_without_publishing_unidentified_item()
    {
        using var capture = new NpcapPacketCapture();
        var scanner = Scanner(capture);
        var missing = new List<RoPacketObservation<RoEquipmentPacket>>();
        var resolved = new List<RoPacketObservation<RoEquipmentPacket>>();
        capture.EquipmentIdentityMissing += missing.Add; capture.EquipmentChanged += resolved.Add;
        scanner.Process(Equip(42));
        var waiting = Assert.Single(missing);
        Assert.Equal(0, waiting.Packet.ItemId); Assert.Empty(resolved);
        scanner.Process(Inventory(42));
        Assert.Equal(2, resolved.Count);
        Assert.Equal(610037, resolved[^1].Packet.ItemId);
        Assert.Equal(waiting.ObservedAtUtc, resolved[^1].ObservedAtUtc);
        Assert.Equal((byte)9, resolved[^1].Packet.RefineLevel);
        Assert.Equal(311281u, resolved[^1].Packet.Card1);
    }

    [Fact]
    public void Inventory_indexes_and_pending_actions_do_not_cross_connections_or_stream_resets()
    {
        using var capture = new NpcapPacketCapture();
        var a = Scanner(capture); var b = Scanner(capture, 50002);
        var missing = new List<RoPacketObservation<RoEquipmentPacket>>();
        var resolved = new List<RoPacketObservation<RoEquipmentPacket>>();
        capture.EquipmentIdentityMissing += missing.Add; capture.EquipmentChanged += resolved.Add;
        a.Process(Inventory(42)); b.Process(Equip(42));
        Assert.Single(resolved); Assert.Single(missing);
        var old = resolved[0]; a.ResetStream();
        Assert.False(capture.IsCurrentObservation(old));
        a.Process(Equip(42)); Assert.Equal(2, missing.Count);
        b.ResetStream(); b.Process(Inventory(42));
        Assert.Equal(2, resolved.Count); // no old pending action is replayed
        Assert.Equal((ushort)0x0B39, resolved[^1].Packet.PacketId);
    }

    [Fact]
    public void Full_35_item_inventory_survives_fragmentation_and_resolves_modern_equip_ack()
    {
        var inventory = Inventory(42, 35);
        Assert.Equal(2385, inventory.Length);
        for (var split = 1; split < inventory.Length; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var items = new List<RoEquipmentPacket>(); scanner.EquipmentChanged += items.Add;
            scanner.Process(inventory.AsSpan(0, split)); scanner.Process(inventory.AsSpan(split));
            scanner.Process(Equip(42));
            Assert.Equal(36, items.Count);
            Assert.Equal(610037, items[^1].ItemId);
            Assert.Equal((byte)9, items[^1].RefineLevel);
        }
    }

    [Fact]
    public void Reused_inventory_index_cannot_override_a_different_learned_fingerprint()
    {
        var incoming = new RoEquipmentPacket(0x0999, 42, 610037, 0x22, true, true, 9, 0, 123);
        var old = new EquipmentTracker { Enabled = true, ItemId = incoming.ItemId, InventoryIndex = 42,
            VariantKey = (incoming with { Card1 = 456 }).VariantKey };
        var match = (List<EquipmentTracker>)typeof(App).GetMethod("FindMatchingEquipmentItems",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [new[] { old }, incoming])!;
        Assert.Empty(match);
    }

    [Fact]
    public void Modern_attack_body_is_opaque_even_when_it_contains_an_equip_signature()
    {
        var attack = new byte[34]; W16(attack, 0, 0x08C8);
        Equip(3072).CopyTo(attack, 10);
        for (var split = 1; split < attack.Length; split++)
        {
            var scanner = new RoPacketStreamScanner(); var equipment = new List<RoEquipmentPacket>();
            var statuses = new List<RoStatusPacket>(); scanner.EquipmentChanged += equipment.Add;
            scanner.StatusChanged += statuses.Add;
            scanner.Process(attack.AsSpan(0, split));
            scanner.Process(attack.AsSpan(split).ToArray().Concat(Status(6, 4200, true)).ToArray());
            Assert.Empty(equipment); Assert.Single(statuses);
        }
    }

    [Fact]
    public void Discovery_ignores_https_even_when_it_is_the_saved_endpoint_and_keeps_loopback()
    {
        var https = new DetectedTcpConnection("Muh", 10, "192.0.2.1", 443);
        var game = new DetectedTcpConnection("Muh", 10, "127.0.0.1", 54729);
        Assert.Equal(game, TcpConnectionDetector.Select([https, game], https.RemoteAddress, 443));
        Assert.Null(TcpConnectionDetector.Select([https]));
        var other = new DetectedTcpConnection("Muh", 20, "192.0.2.2", 5121);
        Assert.Equal(game, TcpConnectionDetector.Select([other, game], other.RemoteAddress, other.RemotePort, 10));
        Assert.Null(TcpConnectionDetector.Select([https, other], preferredProcessId: 10));
    }

    [Fact]
    public void Learning_diagnostics_are_bounded_expire_and_can_be_rearmed()
    {
        var log = new PacketLearningDiagnostics(); var now = DateTime.UtcNow;
        Assert.Null(log.Format(now, "skill", 0x09CB, 138, true, true, true, true));
        log.Begin(now);
        for (var i = 0; i < 64; i++) Assert.NotNull(log.Format(now, "skill", 0x09CB, 138, true, true, false, false));
        Assert.Null(log.Format(now, "skill", 0x09CB, 138, true, true, false, false));
        log.Begin(now);
        Assert.Null(log.Format(now.AddSeconds(20), "skill", 0x09CB, 138, true, true, false, false));
        Assert.Contains("own=not-required", log.Format(now, "post-delay", 0x043D, 138, true, true, false, null));
    }

    private static byte[] Status(ushort id, uint actor, bool active)
    {
        var bytes = new byte[active ? 29 : 9]; W16(bytes, 0, active ? (ushort)0x0983 : (ushort)0x0196);
        W16(bytes, 2, id); W32(bytes, 4, actor); bytes[8] = active ? (byte)1 : (byte)0;
        if (active) { W32(bytes, 9, 165000); W32(bytes, 13, 165000); }
        return bytes;
    }
    private static byte[] Equip(ushort index)
    {
        var bytes = new byte[11]; W16(bytes, 0, 0x0999); W16(bytes, 2, index); W32(bytes, 4, 0x22); return bytes;
    }
    private static byte[] Inventory(ushort firstIndex, int count = 1)
    {
        var bytes = new byte[5 + 68 * count]; W16(bytes, 0, 0x0B39); W16(bytes, 2, (ushort)bytes.Length);
        for (var i = 0; i < count; i++)
        {
            var at = 5 + 68 * i; W16(bytes, at, (ushort)(firstIndex + i)); W32(bytes, at + 2, 610037);
            W32(bytes, at + 7, 0x22); W32(bytes, at + 11, 0x22); W32(bytes, at + 15, 311281); bytes[at + 65] = 9;
        }
        return bytes;
    }
    private static void W16(byte[] b, int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), v);
    private static void W32(byte[] b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v);
}
