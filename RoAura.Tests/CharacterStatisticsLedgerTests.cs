using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class CharacterStatisticsLedgerTests
{
    private static readonly DateTime Start = new(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);
    private static readonly CharacterStatisticsKey A = CharacterStatisticsKey.Create("SpringRO", 154386);
    private static readonly CharacterStatisticsKey B = CharacterStatisticsKey.Create("SpringRO", 155665);

    [Fact]
    public void A_to_B_to_A_keeps_totals_isolated_and_excludes_time_on_other_character()
    {
        WithStore(store =>
        {
            var ledger = new CharacterStatisticsLedger(store);
            ledger.SwitchTo(A, Start);
            ledger.AddExperience(1500000, false, Start);
            ledger.AddExperience(700000, true, Start);
            ledger.AddLoot(501, "Red Potion", 10, Start);
            Assert.Equal(0, ledger.ObserveZeny(5000000, Start));
            Assert.Equal(1500, ledger.ObserveZeny(5001500, Start));
            ledger.SwitchTo(B, Start.AddMinutes(30));
            ledger.AddExperience(2000, false, Start.AddMinutes(30));
            ledger.AddLoot(502, "Orange Potion", 4, Start.AddMinutes(30));
            Assert.Equal(0, ledger.ObserveZeny(100000, Start.AddMinutes(30)));
            Assert.Equal(200, ledger.ObserveZeny(100200, Start.AddMinutes(30)));
            ledger.SwitchTo(A, Start.AddHours(2).AddMinutes(30));
            Assert.Equal(0, ledger.ObserveZeny(5002000, Start.AddHours(2).AddMinutes(30)));
            ledger.AddExperience(1500000, false, Start.AddHours(2).AddMinutes(30));

            var a = ledger.Snapshot(A, Start.AddHours(2).AddMinutes(45))!;
            var b = ledger.Snapshot(B, Start.AddHours(2).AddMinutes(45))!;
            Assert.Equal(3000000, a.SessionBaseExperience);
            Assert.Equal(700000, a.SessionJobExperience);
            Assert.Equal(1500, a.SessionZenyGained);
            Assert.Equal(10, a.SessionLoot[501].Count);
            Assert.Equal(TimeSpan.FromMinutes(45).Ticks, a.SessionActiveTicks);
            Assert.Equal(4000000, a.BaseExperiencePerHour);
            Assert.Equal(2000, b.SessionBaseExperience);
            Assert.Equal(200, b.SessionZenyGained);
            Assert.Equal(TimeSpan.FromHours(2).Ticks, b.SessionActiveTicks);
            Assert.False(b.SessionLoot.ContainsKey(501));

            ledger.Suspend(Start.AddHours(2).AddMinutes(45));
            var restarted = new CharacterStatisticsLedger(store);
            restarted.SwitchTo(A, Start.AddHours(4));
            Assert.Equal(3000000, restarted.Snapshot(A, Start.AddHours(4))!.LifetimeBaseExperience);
            Assert.Equal(TimeSpan.FromMinutes(45).Ticks, restarted.Snapshot(A, Start.AddHours(4))!.SessionActiveTicks);
        });
    }

    [Fact]
    public void Reset_clears_only_active_session_and_does_not_touch_lifetime_or_other_character()
    {
        WithStore(store =>
        {
            var ledger = new CharacterStatisticsLedger(store);
            ledger.SwitchTo(A, Start);
            ledger.AddExperience(100, false, Start);
            ledger.AddLoot(501, "Potion", 3, Start);
            ledger.SwitchTo(B, Start.AddMinutes(1));
            ledger.AddExperience(50, false, Start.AddMinutes(1));
            ledger.SwitchTo(A, Start.AddMinutes(2));
            ledger.ResetCurrentSession(Start.AddMinutes(2));
            var a = ledger.Snapshot(A, Start.AddMinutes(2))!;
            var b = ledger.Snapshot(B, Start.AddMinutes(2))!;
            Assert.Equal(0, a.SessionBaseExperience);
            Assert.Equal(100, a.LifetimeBaseExperience);
            Assert.Empty(a.SessionLoot);
            Assert.Equal(3, a.LifetimeLoot[501].Count);
            Assert.Equal(50, b.SessionBaseExperience);
        });
    }

    [Fact]
    public void Copy_is_immutable_to_caller_and_different_server_uses_different_file()
    {
        WithStore(store =>
        {
            var ledger = new CharacterStatisticsLedger(store);
            ledger.SwitchTo(A, Start);
            ledger.AddLoot(501, "Potion", 3, Start);
            var result = ledger.Snapshot(A, Start)!;
            result.SessionLoot[501].Count = 8888;
            Assert.Equal(3, ledger.Snapshot(A, Start)!.SessionLoot[501].Count);
            ledger.Suspend(Start);
            var other = CharacterStatisticsKey.Create("RTM", A.CharacterId);
            Assert.Null(store.Load(other));
            Assert.NotEqual(store.PathFor(A), store.PathFor(other));
        });
    }

    [Fact]
    public void Corrupt_primary_loads_backup_but_refuses_to_overwrite_it()
    {
        WithStore(store =>
        {
            var first = new CharacterStatisticsDocument { ServerKey = A.ServerKey,
                CharacterId = A.CharacterId, CreatedUtc = Start, SessionStartedUtc = Start };
            store.Save(first);
            first.LifetimeBaseExperience = 50;
            first.SessionBaseExperience = 50;
            store.Save(first);
            File.WriteAllText(store.PathFor(A), "{ invalid json");
            var recovered = store.Load(A)!;
            Assert.Equal(0, recovered.LifetimeBaseExperience);
            Assert.ThrowsAny<Exception>(() => store.Save(recovered));
            Assert.True(File.Exists(store.PathFor(A) + ".bak"));
        });
    }

    [Fact]
    public void Invalid_statistics_values_fall_back_to_backup_without_overwriting_primary()
    {
        WithStore(store =>
        {
            var doc = new CharacterStatisticsDocument
            {
                ServerKey = A.ServerKey, CharacterId = A.CharacterId,
                CreatedUtc = Start, SessionStartedUtc = Start
            };
            store.Save(doc);
            doc.LifetimeBaseExperience = 20;
            doc.SessionBaseExperience = 20;
            store.Save(doc);
            var primary = store.PathFor(A);
            var backup = primary + ".bak";
            // This is valid JSON but invalid statistics; previously it
            // bypassed the backup fallback entirely.
            File.WriteAllText(primary, File.ReadAllText(primary)
                .Replace("\"SessionBaseExperience\": 20", "\"SessionBaseExperience\": -2",
                    StringComparison.Ordinal));
            var recovered = store.Load(A)!;
            Assert.Equal(0, recovered.SessionBaseExperience);
            Assert.True(File.Exists(backup));
            Assert.Throws<InvalidDataException>(() => store.Save(recovered));
            Assert.Contains("\"SessionBaseExperience\": -2", File.ReadAllText(primary));
        });
    }

    [Fact]
    public void Backup_only_character_is_visible_in_history_but_never_auto_restored_over_it()
    {
        WithStore(store =>
        {
            var doc = new CharacterStatisticsDocument
            {
                ServerKey = A.ServerKey, CharacterId = A.CharacterId,
                CreatedUtc = Start, SessionStartedUtc = Start
            };
            store.Save(doc);
            doc.LifetimeJobExperience = 18;
            doc.SessionJobExperience = 18;
            store.Save(doc);
            File.Delete(store.PathFor(A));
            Assert.Contains(A, store.ListKnownCharacters());
            var recovered = store.Load(A)!;
            Assert.Equal(0, recovered.LifetimeJobExperience);
            Assert.Throws<InvalidDataException>(() => store.Save(recovered));
            Assert.False(File.Exists(store.PathFor(A)));
            Assert.True(File.Exists(store.PathFor(A) + ".bak"));
        });
    }

    [Fact]
    public void Newer_schema_and_invalid_identity_cannot_be_overwritten()
    {
        WithStore(store =>
        {
            var doc = new CharacterStatisticsDocument { ServerKey = A.ServerKey,
                CharacterId = A.CharacterId, CreatedUtc = Start, SessionStartedUtc = Start };
            store.Save(doc);
            var path = store.PathFor(A);
            File.WriteAllText(path, File.ReadAllText(path).Replace(
                "\"SchemaVersion\": 1", "\"SchemaVersion\": 500", StringComparison.Ordinal));
            Assert.ThrowsAny<Exception>(() => store.Load(A));
            Assert.ThrowsAny<Exception>(() => store.Save(doc));
            Assert.Throws<ArgumentException>(() =>
                CharacterStatisticsKey.Create("../SpringRO", 100));
        });
    }

    [Fact]
    public void Suspended_or_unknown_character_does_not_receive_rewards()
    {
        WithStore(store =>
        {
            var ledger = new CharacterStatisticsLedger(store);
            Assert.False(ledger.AddExperience(100, false, Start));
            Assert.False(ledger.AddLoot(501, "Potion", 1, Start));
            Assert.Equal(0, ledger.ObserveZeny(1000, Start));
            ledger.SwitchTo(A, Start);
            ledger.Suspend(Start.AddMinutes(1));
            Assert.False(ledger.AddExperience(100, false, Start.AddMinutes(2)));
            Assert.Equal(0, ledger.ObserveZeny(2000, Start.AddMinutes(2)));
        });
    }


    [Fact]
    public void GoldGoblin_manual_rows_and_costs_restore_per_character_and_do_not_change_drop_history()
    {
        WithStore(store =>
        {
            var ledger = new CharacterStatisticsLedger(store);
            ledger.SwitchTo(A, Start);
            ledger.AddLoot(501, "Red Potion", 3, Start);
            ledger.ReplaceGoldGoblinRows(
                [new CharacterLootCount { ItemId = 501, Name = "Red Potion",
                    Count = 9, HasManualCost = true, ManualUnitCost = 77 }], Start);
            ledger.SwitchTo(B, Start.AddSeconds(20));
            ledger.ReplaceGoldGoblinRows(
                [new CharacterLootCount { ItemId = 502, Name = "Orange Potion",
                    Count = 4 }], Start.AddSeconds(20));
            ledger.SwitchTo(A, Start.AddMinutes(1));
            var result = ledger.Snapshot(A, Start.AddMinutes(1))!;
            Assert.Equal(3, result.SessionLoot[501].Count); // actual packet drops
            Assert.Equal(9, result.GoldGoblinRows[501].Count); // manual user edits
            Assert.Equal(77, result.GoldGoblinRows[501].ManualUnitCost);
            Assert.True(result.GoldGoblinRows[501].HasManualCost);
            Assert.False(result.GoldGoblinRows.ContainsKey(502));
            ledger.Suspend(Start.AddMinutes(2));

            var restarted = new CharacterStatisticsLedger(store);
            restarted.SwitchTo(A, Start.AddMinutes(3));
            var restored = restarted.Snapshot(A, Start.AddMinutes(3))!;
            Assert.Equal(9, restored.GoldGoblinRows[501].Count);
            Assert.Equal(77, restored.GoldGoblinRows[501].ManualUnitCost);
            Assert.Equal(3, restored.LifetimeLoot[501].Count);
            restarted.ResetCurrentSession(Start.AddMinutes(3));
            var reset = restarted.Snapshot(A, Start.AddMinutes(3))!;
            Assert.Empty(reset.GoldGoblinRows);
            Assert.Equal(3, reset.LifetimeLoot[501].Count);
        });
    }

    [Fact]
    public void Reconnecting_same_character_creates_fresh_Zeny_baseline()
    {
        WithStore(store =>
        {
            var ledger = new CharacterStatisticsLedger(store);
            ledger.SwitchTo(A, Start);
            Assert.Equal(0, ledger.ObserveZeny(1_000_000, Start));
            Assert.Equal(700, ledger.ObserveZeny(1_000_700, Start));
            ledger.Suspend(Start.AddMinutes(5));
            ledger.SwitchTo(A, Start.AddMinutes(6));
            Assert.Equal(0, ledger.ObserveZeny(9_000_000, Start.AddMinutes(6)));
            Assert.Equal(500, ledger.ObserveZeny(9_000_500, Start.AddMinutes(6)));
            var stats = ledger.Snapshot(A, Start.AddMinutes(7))!;
            Assert.Equal(1200, stats.SessionZenyGained);
            // Zeny alone must not start the unified session clock.
            Assert.False(stats.SessionTimerStarted);
            Assert.Equal(0, stats.SessionActiveTicks);
            ledger.AddExperience(100, false, Start.AddMinutes(8));
            Assert.Equal(TimeSpan.FromMinutes(1).Ticks,
                ledger.Snapshot(A, Start.AddMinutes(9))!.SessionActiveTicks);
        });
    }


    [Fact]
    public void Shared_clock_starts_on_first_EXP_not_login_loot_or_Zeny()
    {
        WithStore(store =>
        {
            var ledger = new CharacterStatisticsLedger(store);
            ledger.SwitchTo(A, Start);
            ledger.AddLoot(501, "Potion", 2, Start.AddMinutes(5));
            ledger.ObserveZeny(1000, Start.AddMinutes(8));
            var before = ledger.Snapshot(A, Start.AddMinutes(10))!;
            Assert.False(before.SessionTimerStarted);
            Assert.Equal(0, before.SessionActiveTicks);

            ledger.AddExperience(100, true, Start.AddMinutes(12));
            var active = ledger.Snapshot(A, Start.AddMinutes(13))!;
            Assert.True(active.SessionTimerStarted);
            Assert.Equal(Start.AddMinutes(12), active.SessionStartedUtc);
            Assert.Equal(TimeSpan.FromMinutes(1).Ticks, active.SessionActiveTicks);
            ledger.SwitchTo(B, Start.AddMinutes(13));
            ledger.AddLoot(502, "Orange Potion", 1, Start.AddMinutes(14));
            Assert.False(ledger.Snapshot(B, Start.AddMinutes(15))!.SessionTimerStarted);
            ledger.SwitchTo(A, Start.AddMinutes(30));
            Assert.Equal(TimeSpan.FromMinutes(2).Ticks,
                ledger.Snapshot(A, Start.AddMinutes(31))!.SessionActiveTicks);
            Assert.False(ledger.Snapshot(B, Start.AddMinutes(31))!.SessionTimerStarted);
        });
    }

    [Fact]
    public void Manual_start_uses_same_timer_and_resets_only_active_character()
    {
        WithStore(store =>
        {
            var ledger = new CharacterStatisticsLedger(store);
            ledger.SwitchTo(A, Start);
            ledger.AddLoot(501, "Potion", 1, Start);
            ledger.StartNewSession(Start.AddMinutes(10));
            Assert.True(ledger.Snapshot(A, Start.AddMinutes(10))!.SessionTimerStarted);
            Assert.Equal(0, ledger.Snapshot(A, Start.AddMinutes(10))!.SessionActiveTicks);
            Assert.Empty(ledger.Snapshot(A, Start.AddMinutes(10))!.SessionLoot);
            Assert.Equal(TimeSpan.FromMinutes(3).Ticks,
                ledger.Snapshot(A, Start.AddMinutes(13))!.SessionActiveTicks);
            ledger.ResetCurrentSession(Start.AddMinutes(13));
            Assert.False(ledger.Snapshot(A, Start.AddMinutes(15))!.SessionTimerStarted);
            Assert.Equal(0, ledger.Snapshot(A, Start.AddMinutes(15))!.SessionActiveTicks);
            ledger.AddExperience(25, false, Start.AddMinutes(16));
            Assert.Equal(TimeSpan.FromMinutes(1).Ticks,
                ledger.Snapshot(A, Start.AddMinutes(17))!.SessionActiveTicks);
        });
    }

    [Fact]
    public void Started_clock_survives_restart_without_counting_offline_time()
    {
        WithStore(store =>
        {
            var ledger = new CharacterStatisticsLedger(store);
            ledger.SwitchTo(A, Start);
            ledger.AddExperience(100, false, Start.AddMinutes(3));
            ledger.Suspend(Start.AddMinutes(9));
            var restarted = new CharacterStatisticsLedger(store);
            restarted.SwitchTo(A, Start.AddHours(2));
            var snapshot = restarted.Snapshot(A, Start.AddHours(2).AddMinutes(4))!;
            Assert.True(snapshot.SessionTimerStarted);
            Assert.Equal(TimeSpan.FromMinutes(10).Ticks, snapshot.SessionActiveTicks);
        });
    }

    private static void WithStore(Action<CharacterStatisticsStore> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoAura-stats-test-" + Guid.NewGuid().ToString("N"));
        try { check(new CharacterStatisticsStore(directory)); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
