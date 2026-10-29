using System.Buffers.Binary;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class SpringRoCorrelatedHidingTrackerTests
{
    private const uint OwnAid = 2004474;

    private static RoPacketLayoutProbe Probe(byte[] frame, long offset, bool boundary = true) =>
        Assert.IsType<RoPacketLayoutProbe>(RoPacketLayoutProbe.Create(frame, offset, boundary));

    private static byte[] TimedStart(ushort scrambledStatus = 43210,
        ushort scrambledActorLow = 28001, uint duration = 240000, byte state = 1,
        uint highActor = OwnAid)
    {
        var b = new byte[29];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 0x0983);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), scrambledStatus);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), scrambledActorLow);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), (ushort)(highActor >> 16));
        b[8] = state;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(9), duration);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(13), duration);
        return b;
    }

    private static byte[] StatusEnd(ushort scrambledStatus = 43000,
        ushort scrambledActorLow = 21943, byte state = 0, uint highActor = OwnAid)
    {
        var b = new byte[9];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 0x0196);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), scrambledStatus);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), scrambledActorLow);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), (ushort)(highActor >> 16));
        b[8] = state;
        return b;
    }

    private static byte[] Option(bool hidden, uint actor = OwnAid)
    {
        var b = new byte[15];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 0x0229);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(2), actor);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), hidden ? 2u : 0u);
        return b;
    }

    private static byte[] Skill(ushort id = 51, uint actor = OwnAid, byte result = 1)
    {
        var b = new byte[17];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 0x09CB);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), id);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), actor);
        b[^1] = result;
        return b;
    }

    [Fact]
    public void Self_verified_transitions_alternate_even_with_scrambled_status_and_actor_low()
    {
        var tracker = new SpringRoCorrelatedHidingTracker();
        for (int i = 0; i < 4; i++)
        {
            long start = 500 + i * 200;
            Assert.Null(tracker.Observe(Probe(TimedStart((ushort)(20000 + i * 643),
                (ushort)(1000 + i * 871)), start), OwnAid));
            Assert.True(tracker.Observe(Probe(Skill(), start + 29), OwnAid));

            long end = start + 110;
            Assert.Null(tracker.Observe(Probe(StatusEnd((ushort)(30000 + i * 53),
                (ushort)(22000 + i * 19)), end), OwnAid));
            // Correlated end wins even though the server sends an incorrect hideBit.
            Assert.Null(tracker.Observe(Probe(Option(hidden: true), end + 9), OwnAid));
            Assert.False(tracker.Observe(Probe(Skill(), end + 24), OwnAid));
        }
    }

    [Fact]
    public void Rejected_unverified_foreign_failed_or_wrong_skill_and_wrong_duration()
    {
        var tracker = new SpringRoCorrelatedHidingTracker();

        tracker.Observe(Probe(TimedStart(), 100), 0);
        Assert.Null(tracker.Observe(Probe(Skill(), 129), 0));
        tracker.Observe(Probe(TimedStart(), 200), OwnAid);
        Assert.Null(tracker.Observe(Probe(Skill(actor: OwnAid + 1), 229), OwnAid));
        tracker.Observe(Probe(TimedStart(), 300), OwnAid);
        Assert.Null(tracker.Observe(Probe(Skill(id: 138), 329), OwnAid));
        tracker.Observe(Probe(TimedStart(), 400), OwnAid);
        Assert.Null(tracker.Observe(Probe(Skill(result: 0), 429), OwnAid));
        tracker.Observe(Probe(TimedStart(duration: 180000), 500), OwnAid);
        Assert.Null(tracker.Observe(Probe(Skill(), 529), OwnAid));
        tracker.Observe(Probe(TimedStart(state: 0), 600), OwnAid);
        Assert.Null(tracker.Observe(Probe(Skill(), 629), OwnAid));
        tracker.Observe(Probe(TimedStart(highActor: 0x12340000), 700), OwnAid);
        Assert.Null(tracker.Observe(Probe(Skill(), 729), OwnAid));
    }

    [Fact]
    public void Rejects_gaps_wrong_options_midstream_boundaries_and_reset()
    {
        var tracker = new SpringRoCorrelatedHidingTracker();
        tracker.Observe(Probe(StatusEnd(), 100), OwnAid);
        Assert.Null(tracker.Observe(Probe(Option(false, OwnAid + 1), 109), OwnAid));
        Assert.Null(tracker.Observe(Probe(Skill(), 124), OwnAid));
        tracker.Observe(Probe(TimedStart(), 200), OwnAid);
        Assert.Null(tracker.Observe(Probe(Skill(), 230), OwnAid));
        tracker.Observe(Probe(TimedStart(), 300), OwnAid);
        Assert.Null(tracker.Observe(Probe(Skill(), 329, boundary: false), OwnAid));
        tracker.Observe(Probe(TimedStart(), 400), OwnAid);
        tracker.Reset();
        Assert.Null(tracker.Observe(Probe(Skill(), 429), OwnAid));
    }

    [Fact]
    public void Synthesized_Hiding_status_moves_shared_effect_and_reminder_timeline()
    {
        var tracker = new SpringRoCorrelatedHidingTracker();
        var timeline = new StatusTrackingController();
        var effect = new TrackedEffect { StatusId = 4 };
        var reminder = new ReminderItem { StatusId = 4 };
        var now = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);

        tracker.Observe(Probe(TimedStart(), 0), OwnAid);
        var appeared = Assert.IsType<bool>(tracker.Observe(Probe(Skill(), 29), OwnAid));
        Assert.True(appeared);
        timeline.Observe(new RoStatusPacket(0x0983, 4, OwnAid, appeared, 0, 0), now);
        Assert.Single(timeline.Synchronize([effect], [reminder], now, "ru"));
        Assert.True(effect.IsActive);
        Assert.False(reminder.IsVisible);

        tracker.Observe(Probe(StatusEnd(), 100), OwnAid);
        tracker.Observe(Probe(Option(true), 109), OwnAid);
        var disappeared = Assert.IsType<bool>(tracker.Observe(Probe(Skill(), 124), OwnAid));
        Assert.False(disappeared);
        timeline.Observe(new RoStatusPacket(0x0196, 4, OwnAid, disappeared, 0, 0),
            now.AddSeconds(1));
        Assert.Single(timeline.Synchronize([effect], [reminder], now.AddSeconds(1), "ru"));
        Assert.False(effect.IsActive);
        Assert.True(reminder.IsVisible);
    }
}
