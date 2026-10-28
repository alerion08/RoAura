using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class PacketTrackingPipelineTests
{
    private static CharacterSessionFlow Flow(ushort port) => new(1, port, 2, 57143);

    [Fact]
    public void Hiding_state_is_per_actor_and_does_not_retrigger_after_other_actor_changes()
    {
        var hiding = new HidingStateTracker();
        Assert.True(hiding.Observe(new(0x0229, 4200, 2)));
        Assert.True(hiding.Observe(new(0x0229, 9000, 2)));
        Assert.False(hiding.Observe(new(0x0229, 9000, 0)));
        Assert.False(hiding.Observe(new(0x0229, 4200, 2)));
        Assert.False(hiding.Observe(new(0x0229, 4200, 0)));
        Assert.True(hiding.Observe(new(0x0229, 4200, 2)));
    }

    [Fact]
    public void Capture_envelope_keeps_same_flow_identity_across_other_connection_activity()
    {
        using var capture = new NpcapPacketCapture();
        var a = Scanner(capture, Flow(50001));
        var b = Scanner(capture, Flow(50002));
        Identity(capture).Observe(Flow(50001), 4200, "EXP");
        Identity(capture).Observe(Flow(50002), 9000, "ZC_AID");
        var observations = new List<RoPacketObservation<RoStatusPacket>>();
        capture.StatusChanged += observations.Add;
        a.Process(Status(4200));
        b.Process(Status(9000));
        Assert.Equal(4200u, observations[0].SelfAid);
        Assert.Equal(9000u, observations[1].SelfAid);
        Assert.True(observations[0].IsOwnActor(4200));
        Assert.False(observations[0].IsOwnActor(9000));
        Assert.NotSame(observations[0].Source, observations[1].Source);
    }

    [Fact]
    public void Decoding_without_identity_is_observable_but_does_not_claim_self()
    {
        using var capture = new NpcapPacketCapture();
        var scanner = Scanner(capture, Flow(50001));
        var statuses = new List<RoPacketObservation<RoStatusPacket>>();
        var cooldowns = new List<RoPacketObservation<RoSkillCooldownPacket>>();
        capture.StatusChanged += statuses.Add;
        capture.SkillCooldown += cooldowns.Add;
        scanner.Process(Status(4200));
        Assert.Equal((ushort)34, Assert.Single(statuses).Packet.StatusId);
        Assert.Equal(0u, statuses[0].SelfAid);
        Assert.True(capture.IsCurrentObservation(statuses[0]));
        Assert.False(statuses[0].IsOwnActor(4200));
        scanner.Process(new byte[] { 0x3D, 0x04, 51, 0, 0xE8, 3, 0, 0 });
        Assert.True(capture.IsCurrentObservation(Assert.Single(cooldowns)));
        Assert.Equal((ushort)51, cooldowns[0].Packet.SkillId);
    }

    [Fact]
    public void Queued_observation_is_invalid_after_stream_reset_even_on_same_flow()
    {
        using var capture = new NpcapPacketCapture();
        var scanner = Scanner(capture, Flow(50001));
        var packets = new List<RoPacketObservation<RoStatusPacket>>();
        capture.StatusChanged += packets.Add;
        scanner.Process(Status(4200));
        var old = Assert.Single(packets);
        scanner.ResetStream();
        Assert.False(capture.IsCurrentObservation(old));
        scanner.Process(Status(4200));
        Assert.True(capture.IsCurrentObservation(packets[1]));
    }

    [Fact]
    public void Known_character_flow_rejects_other_connection_even_with_same_actor_id()
    {
        using var capture = new NpcapPacketCapture();
        var a = Scanner(capture, Flow(50001));
        var b = Scanner(capture, Flow(50002));
        var sessions = (CharacterSessionManager)typeof(NpcapPacketCapture)
            .GetField("_characterSessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        sessions.OnCandidate("SpringRO", Flow(50002), 123, 4200);
        sessions.OnServerPayload(Flow(50002));
        var packets = new List<RoPacketObservation<RoStatusPacket>>();
        capture.StatusChanged += packets.Add;
        a.Process(Status(4200)); b.Process(Status(4200));
        Assert.False(capture.IsCurrentObservation(packets[0]));
        Assert.True(capture.IsCurrentObservation(packets[1]));
    }

    [Fact]
    public void Captured_identity_is_not_reinterpreted_at_dispatch_time()
    {
        using var capture = new NpcapPacketCapture();
        var scanner = Scanner(capture, Flow(50001));
        var packets = new List<RoPacketObservation<RoStatusPacket>>();
        capture.StatusChanged += packets.Add;
        scanner.Process(Status(4200));
        Identity(capture).Observe(Flow(50001), 4200, "EXP");
        Assert.Equal(0u, Assert.Single(packets).SelfAid);
    }

    [Fact]
    public void Status_waiting_for_identity_replays_only_self_with_original_time_and_latest_state()
    {
        var pending = new PendingStatusObservations();
        var source = new RoPacketSource(0, Flow(50001));
        var start = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        pending.Remember(new(new(0x0983, 34, 4200, true, 1000, 1000), source, start, 0, ""));
        pending.Remember(new(new(0x0196, 34, 4200, false, 0, 0), source, start.AddMilliseconds(100), 0, ""));
        pending.Remember(new(new(0x0983, 34, 9000, true, 1000, 1000), source, start, 0, ""));
        var observation = Assert.Single(pending.Resolve(source, 4200, "EXP", start.AddMilliseconds(500)));
        Assert.False(observation.Packet.Active);
        Assert.Equal(start.AddMilliseconds(100), observation.ObservedAtUtc);
        Assert.True(observation.IsOwnActor(4200));
        Assert.Empty(pending.Resolve(source, 4200, "EXP", start.AddSeconds(1)));
    }

    [Fact]
    public void Status_before_coalesced_self_aid_is_replayed_after_identity_becomes_available()
    {
        using var capture = new NpcapPacketCapture();
        var scanner = Scanner(capture, Flow(50001));
        var packets = new List<RoPacketObservation<RoStatusPacket>>();
        capture.StatusChanged += packets.Add;
        scanner.Process(Status(4200).Concat(new byte[] { 0x83, 0x02, 0x68, 0x10, 0, 0 }).ToArray());
        if (!AppEnvironment.IsDevelopment) return; // Self evidence is deliberately DEV-only.
        Assert.Equal(2, packets.Count);
        Assert.Equal(0u, packets[0].SelfAid);
        Assert.True(packets[1].IsOwnActor(4200));
        Assert.Equal(packets[0].ObservedAtUtc, packets[1].ObservedAtUtc);
    }

    [Fact]
    public void Pending_identity_does_not_replay_old_or_closed_flow_statuses()
    {
        var pending = new PendingStatusObservations();
        var source = new RoPacketSource(0, Flow(50001));
        var start = DateTime.UtcNow;
        pending.Remember(new(new(0x0196, 4, 4200, true, 0, 0), source, start, 0, ""));
        Assert.Empty(pending.Resolve(source, 4200, "EXP", start.AddMinutes(3)));
        pending.Remember(new(new(0x0196, 4, 4200, true, 0, 0), source, start, 0, ""));
        source.Close();
        Assert.Empty(pending.Resolve(source, 4200, "EXP", start));
    }

    private static PlayerAidObserver Identity(NpcapPacketCapture capture) =>
        (PlayerAidObserver)typeof(NpcapPacketCapture).GetField("_playerAidObserver",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;

    private static RoPacketStreamScanner Scanner(NpcapPacketCapture capture, CharacterSessionFlow flow) =>
        (RoPacketStreamScanner)typeof(NpcapPacketCapture).GetMethod("CreateScanner",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(capture,
            [0, new ConcurrentDictionary<ushort, RoEquipmentPacket>(),
                new ConcurrentDictionary<ushort, RoEquipmentPacket>(), flow])!;

    private static byte[] Status(uint actor)
    {
        var bytes = new byte[9];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0x0196);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 34);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), actor);
        bytes[8] = 1;
        return bytes;
    }
}
