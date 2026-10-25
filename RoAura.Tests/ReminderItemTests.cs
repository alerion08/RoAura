using RoAura.Models;
using Xunit;

namespace RoAura.Tests;

public sealed class ReminderItemTests
{
    [Fact]
    public void ReminderIsVisibleOnlyWhileTrackedStatusIsMissing()
    {
        var reminder = new ReminderItem { Enabled = true, IsStatusPresent = false };

        Assert.True(reminder.IsVisible);

        reminder.IsStatusPresent = true;
        Assert.False(reminder.IsVisible);

        reminder.IsStatusPresent = false;
        Assert.True(reminder.IsVisible);
    }

    [Fact]
    public void PlacementPreviewDoesNotChangeTrackedStatus()
    {
        var reminder = new ReminderItem { Enabled = true, IsStatusPresent = true };

        reminder.IsPlacementPreview = true;

        Assert.True(reminder.IsVisible);
        Assert.True(reminder.IsStatusPresent);
    }
}
