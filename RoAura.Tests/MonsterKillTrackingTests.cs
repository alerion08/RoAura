using System.Buffers.Binary;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class MonsterKillTrackingTests
{
    [Fact]
    public void Split_spawn_attack_and_death_produce_one_kill()
    {
        const uint self = 123;
        const uint target = 456;
        const ushort poring = 1002;
        var spawn = new byte[107];
        Write16(spawn, 0, 0x09FE);
        Write16(spawn, 2, (ushort)spawn.Length);
        spawn[4] = 5;
        Write32(spawn, 5, target);
        Write16(spawn, 23, poring);
        var attack = new byte[34];
        Write16(attack, 0, 0x08C8);
        Write32(attack, 2, self);
        Write32(attack, 6, target);
        Write32(attack, 22, 50);
        var death = new byte[7];
        Write16(death, 0, 0x0080);
        Write32(death, 2, target);
        death[6] = 1;

        var stream = spawn.Concat(attack).Concat(death).Concat(death).ToArray();
        for (var split = 1; split < stream.Length; split++)
        {
            var scanner = new RoPacketStreamScanner();
            var tracker = new MonsterKillTracker();
            var kills = new List<ushort>();
            var now = DateTime.UtcNow;
            scanner.MonsterCombatObserved += packet =>
            {
                if (tracker.Observe(packet, self, now) is { } id) kills.Add(id);
            };
            scanner.Process(stream.AsSpan(0, split));
            scanner.Process(stream.AsSpan(split));
            Assert.Equal(poring, Assert.Single(kills));
        }
    }

    [Theory]
    [InlineData((int)MonsterCombatEventKind.Damage, 99u, 0)]
    [InlineData((int)MonsterCombatEventKind.Removed, 0u, 0)]
    [InlineData((int)MonsterCombatEventKind.MapChanged, 0u, 0)]
    [InlineData((int)MonsterCombatEventKind.Spawn, 0u, 0)]
    [InlineData((int)MonsterCombatEventKind.Damage, 10u, 4)]
    public void Death_ignores_other_attackers_despawn_reused_actors_and_stale_hits(
        int next, uint attacker, int elapsedSeconds)
    {
        var tracker = new MonsterKillTracker();
        var now = DateTime.UtcNow;
        tracker.Observe(new(MonsterCombatEventKind.Spawn, 20, MonsterId: 1002), 10, now);
        tracker.Observe(new(MonsterCombatEventKind.Damage, 20, 10), 10, now);
        tracker.Observe(new((MonsterCombatEventKind)next, 20, attacker, 1002), 10, now);
        Assert.Null(tracker.Observe(new(MonsterCombatEventKind.Death, 20), 10, now.AddSeconds(elapsedSeconds)));
    }

    [Fact]
    public void Local_counters_are_character_bound_and_survive_reset_and_reload()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CharacterStatisticsStore(directory);
            var ledger = new CharacterStatisticsLedger(store);
            var now = DateTime.UtcNow;
            var first = CharacterStatisticsKey.Create("SpringRO", 1);
            var second = CharacterStatisticsKey.Create("SpringRO", 2);
            ledger.SwitchTo(first, now);
            Assert.True(ledger.AddMonsterKill(1002, now));
            ledger.SetPaused(true, now);
            Assert.False(ledger.AddMonsterKill(1002, now));
            ledger.SwitchTo(second, now);
            Assert.True(ledger.AddMonsterKill(1003, now));
            ledger.Flush(now);

            var reloaded = new CharacterStatisticsLedger(store);
            reloaded.SwitchTo(first, now);
            Assert.Equal(1L, reloaded.Snapshot(first, now)!.SessionMonsterKills[1002]);
            reloaded.ResetCurrentSession(now);
            Assert.Empty(store.Load(first)!.SessionMonsterKills);
            Assert.Equal(1L, store.Load(first)!.LifetimeMonsterKills[1002]);
            Assert.Equal(1L, store.Load(second)!.LifetimeMonsterKills[1003]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void Write16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);

    private static void Write32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
}
