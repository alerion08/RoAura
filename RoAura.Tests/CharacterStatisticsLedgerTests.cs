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

    private static void WithStore(Action<CharacterStatisticsStore> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoAura-stats-test-" + Guid.NewGuid().ToString("N"));
        try { check(new CharacterStatisticsStore(directory)); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
