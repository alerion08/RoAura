using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class EffectNotificationStateTests
{
    [Fact]
    public void Rapid_Hiding_reactivation_removes_ended_message_even_when_repeat_is_suppressed()
    {
        var tracker = new StatusTrackingController();
        var effect = new TrackedEffect { StatusId = 4, RepeatProtectionSeconds = 2 };
        var now = DateTime.UtcNow;
        tracker.Observe(new(0x0229, 4, 123, true, 0, 0), now);
        Assert.Single(tracker.Synchronize([effect], [], now, "ru"));
        tracker.Observe(new(0x0229, 4, 123, false, 0, 0), now.AddMilliseconds(500));
        Assert.Single(tracker.Synchronize([effect], [], now.AddMilliseconds(500), "ru"));
        var ended = new EffectNotification { EffectId = effect.Id, Appeared = false };
        Assert.True(ended.MatchesCurrentState(effect));
        tracker.Observe(new(0x0229, 4, 123, true, 0, 0), now.AddMilliseconds(800));
        Assert.Empty(tracker.Synchronize([effect], [], now.AddMilliseconds(800), "ru"));
        Assert.True(effect.IsActive);
        Assert.False(ended.MatchesCurrentState(effect));
    }

    [Fact]
    public void Disabled_opposite_notification_still_invalidates_old_message_but_preserves_preview()
    {
        var effect = new TrackedEffect { IsActive = true, NotifyOnDisappeared = false };
        var appeared = new EffectNotification { EffectId = effect.Id, Appeared = true };
        Assert.True(appeared.MatchesCurrentState(effect));
        effect.IsActive = false;
        Assert.False(appeared.MatchesCurrentState(effect));
        Assert.False(appeared.MatchesCurrentState(null));
        Assert.True(new EffectNotification { IsPlacementPreview = true }.MatchesCurrentState(null));
        Assert.True(new EffectNotification { FollowsStatus = false }.MatchesCurrentState(effect));
    }
}
