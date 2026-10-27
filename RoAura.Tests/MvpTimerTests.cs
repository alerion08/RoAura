using RoAura.Models;
using Xunit;

namespace RoAura.Tests;

public sealed class MvpTimerTests
{
    [Fact]
    public void New_timer_is_not_started_and_not_visible()
    {
        var timer = new MvpTimer();

        Assert.Equal("TimerNotStarted", timer.StateText);
        Assert.False(timer.IsRunning);
        Assert.False(timer.IsVisible);
    }

    [Fact]
    public void Restart_sets_utc_end_and_makes_timer_visible()
    {
        var before = DateTime.UtcNow;
        var timer = new MvpTimer { DurationMinutes = 30 };

        timer.Restart();

        Assert.True(timer.EndsAtUtc >= before.AddMinutes(30));
        Assert.InRange(timer.RemainingSeconds, 1799, 1800);
        Assert.NotNull(timer.KilledAtUtc);
        Assert.True(timer.IsRunning);
        Assert.True(timer.IsVisible);
        Assert.Equal("TimerRunning", timer.StateText);
    }

    [Fact]
    public void Reset_stops_timer_without_changing_duration()
    {
        var timer = new MvpTimer { DurationMinutes = 45 };
        timer.Restart();

        timer.Reset();

        Assert.Null(timer.EndsAtUtc);
        Assert.Null(timer.KilledAtUtc);
        Assert.Empty(timer.KillTimeInput);
        Assert.Equal(0, timer.RemainingSeconds);
        Assert.Equal(45, timer.DurationMinutes);
        Assert.Equal("TimerNotStarted", timer.StateText);
    }

    [Fact]
    public void Elapsed_timer_is_completed_and_hidden()
    {
        var timer = new MvpTimer
        {
            EndsAtUtc = DateTime.UtcNow.AddMinutes(-1),
            RemainingSeconds = 0
        };

        Assert.Equal("TimerCompleted", timer.StateText);
        Assert.False(timer.IsVisible);
    }

    [Fact]
    public void Exact_kill_time_drives_end_time_and_is_preserved()
    {
        var localKillTime = DateTime.SpecifyKind(DateTime.Now.AddMinutes(-5), DateTimeKind.Local);
        var timer = new MvpTimer { DurationMinutes = 60 };

        timer.StartFromKillTime(localKillTime);

        Assert.Equal(localKillTime.ToUniversalTime(), timer.KilledAtUtc);
        Assert.Equal(timer.KilledAtUtc.Value.AddMinutes(60), timer.EndsAtUtc);
        Assert.InRange(timer.RemainingSeconds, 3299, 3300);
        Assert.True(timer.HasKillTime);
        Assert.NotEmpty(timer.KilledAtText);
    }

    [Fact]
    public void Kill_clock_after_midnight_uses_previous_day()
    {
        var now = new DateTime(2026, 10, 27, 0, 10, 0, DateTimeKind.Local);
        var timer = new MvpTimer { DurationMinutes = 60 };

        timer.StartFromKillClock(new TimeSpan(23, 50, 0), now);

        Assert.Equal(now.Date.AddDays(-1).AddHours(23).AddMinutes(50).ToUniversalTime(), timer.KilledAtUtc);
        Assert.Equal("23:50:00", timer.KilledAtText);
        Assert.Equal("23:50:00", timer.KillTimeInput);
    }

    [Fact]
    public void Monster_details_supply_respawn_hints_to_existing_timer()
    {
        var timer = new MvpTimer { MonsterId = 1039 };
        var monster = new MonsterEntry
        {
            Id = 1039,
            Spawns =
            [
                new MonsterSpawnEntry { Code = "prt_maze03", Name = "Labyrinth Forest", Respawn = "120–130 min" }
            ]
        };

        timer.SetMonsterDetails(monster);

        var hint = Assert.Single(timer.RespawnHints);
        Assert.Equal("prt_maze03", hint.Code);
        Assert.Equal("120–130 min", hint.Respawn);
    }
}
