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
        Assert.Equal(1800, timer.RemainingSeconds, 3);
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
}
