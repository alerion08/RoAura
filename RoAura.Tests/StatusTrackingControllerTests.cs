using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class StatusTrackingControllerTests
{
    private static readonly DateTime Start = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private static RoStatusPacket Buff(uint remaining = 1000, bool active = true, ushort id = 34) =>
        new(0x0983, id, 4200, active, remaining, 60000);

    [Fact]
    public void Reminder_without_effect_row_expires_and_reapplication_moves_the_deadline()
    {
        var tracker = new StatusTrackingController();
        var reminder = new ReminderItem { StatusId = 34 };
        tracker.Observe(Buff(), Start);
        tracker.Synchronize([], [reminder], Start, "en");
        Assert.False(reminder.IsVisible);
        tracker.Observe(Buff(2000), Start.AddMilliseconds(500));
        tracker.Synchronize([], [reminder], Start.AddSeconds(1), "en");
        Assert.False(reminder.IsVisible);
        tracker.Synchronize([], [reminder], Start.AddMilliseconds(2500), "en");
        Assert.True(reminder.IsVisible);
    }

    [Fact]
    public void Learning_replay_and_duplicate_rows_share_deadline_and_notify_each_transition_once()
    {
        var tracker = new StatusTrackingController();
        var a = new TrackedEffect { StatusId = 34 };
        var b = new TrackedEffect { StatusId = 34 };
        var reminder = new ReminderItem { StatusId = 34 };
        tracker.Observe(Buff(), Start);
        Assert.Equal(2, tracker.Synchronize([a, b], [reminder], Start, "en").Count);
        tracker.Observe(Buff(), Start); // typed replay during learning
        Assert.Empty(tracker.Synchronize([a, b], [reminder], Start.AddMilliseconds(500), "en"));
        Assert.Equal(0.5, a.RemainingSeconds);
        Assert.Equal(60, a.TotalDurationSeconds);
        Assert.Equal(2, tracker.Synchronize([a, b], [reminder], Start.AddSeconds(1), "en").Count);
        Assert.False(a.IsActive);
        Assert.True(reminder.IsVisible);
        tracker.Observe(Buff(0, false), Start.AddSeconds(2));
        Assert.Empty(tracker.Synchronize([a, b], [reminder], Start.AddSeconds(2), "en"));
    }

    [Fact]
    public void Wizard_created_after_packet_uses_original_deadline_and_does_not_resurrect_expired_buff()
    {
        var tracker = new StatusTrackingController();
        tracker.Observe(Buff(), Start);
        tracker.Synchronize([], [], Start, "ru");
        var effect = new TrackedEffect { StatusId = 34 };
        var reminder = new ReminderItem { StatusId = 34 };
        Assert.Single(tracker.Synchronize([effect], [reminder], Start.AddMilliseconds(400), "ru"));
        Assert.Equal(0.6, effect.RemainingSeconds);
        tracker.Synchronize([effect], [reminder], Start.AddSeconds(3), "ru");
        Assert.False(effect.IsActive);
        Assert.True(reminder.IsVisible);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void Unknown_duration_waits_for_server_removal(uint duration)
    {
        var tracker = new StatusTrackingController();
        var reminder = new ReminderItem { StatusId = 4 };
        tracker.Observe(Buff(duration, id: 4), Start);
        tracker.Synchronize([], [reminder], Start.AddDays(1), "en");
        Assert.False(reminder.IsVisible);
        tracker.Observe(Buff(0, false, 4), Start.AddDays(1));
        tracker.Synchronize([], [reminder], Start.AddDays(1), "en");
        Assert.True(reminder.IsVisible);
    }

    [Fact]
    public void Repeated_Hiding_option_does_not_erase_server_duration()
    {
        var tracker = new StatusTrackingController();
        tracker.Observe(Buff(1000, id: 4), Start);
        tracker.Observe(new(0x0229, 4, 4200, true, 0, 0), Start.AddMilliseconds(100));
        Assert.True(tracker.IsPresent(4, Start.AddMilliseconds(500)));
        Assert.False(tracker.IsPresent(4, Start.AddSeconds(1)));
    }

    [Fact]
    public void Expiry_does_not_depend_on_disabled_effect_or_hidden_notifications()
    {
        var tracker = new StatusTrackingController();
        var effect = new TrackedEffect { StatusId = 34, Enabled = false };
        var reminder = new ReminderItem { StatusId = 34, Enabled = false };
        tracker.Observe(Buff(), Start);
        Assert.Empty(tracker.Synchronize([effect], [reminder], Start, "en", false));
        Assert.True(reminder.IsStatusPresent);
        reminder.Enabled = true;
        Assert.Empty(tracker.Synchronize([effect], [reminder], Start.AddSeconds(2), "en", false));
        Assert.True(reminder.IsVisible);
        Assert.False(effect.IsActive);
    }

    [Fact]
    public void Changing_status_id_and_reset_cannot_reuse_previous_active_state()
    {
        var tracker = new StatusTrackingController();
        var effect = new TrackedEffect { StatusId = 34 };
        var reminder = new ReminderItem { StatusId = 34 };
        tracker.Observe(Buff(10000), Start);
        tracker.Synchronize([effect], [reminder], Start, "en");
        effect.StatusId = reminder.StatusId = 35;
        Assert.Empty(tracker.Synchronize([effect], [reminder], Start, "en"));
        Assert.False(effect.IsActive);
        Assert.True(reminder.IsVisible);
        tracker.Reset();
        effect.StatusId = reminder.StatusId = 34;
        Assert.Empty(tracker.Synchronize([effect], [reminder], Start, "en"));
        Assert.False(effect.IsActive);
        Assert.True(reminder.IsVisible);
    }

    [Fact]
    public void Out_of_order_learning_replay_cannot_undo_removal()
    {
        var tracker = new StatusTrackingController();
        tracker.Observe(Buff(), Start);
        tracker.Observe(Buff(0, false), Start.AddMilliseconds(100));
        tracker.Observe(Buff(), Start);
        Assert.False(tracker.IsPresent(34, Start.AddMilliseconds(200)));
    }
}
