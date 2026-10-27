using RoAura.Models;

namespace RoAura.Services;

/// <summary>
/// Converts coordinates written by the old virtual-desktop overlay into
/// coordinates relative to the Ragnarok client area. The persisted marker
/// makes the operation safe to call on every overlay startup.
/// </summary>
public static class OverlayCoordinateMigration
{
    public static bool Apply(AppSettings settings, double gameOffsetX, double gameOffsetY)
    {
        if (settings.OverlayCoordinatesRelativeToGame) return false;

        settings.NotificationX -= gameOffsetX;
        settings.NotificationY -= gameOffsetY;
        foreach (var skill in settings.Skills)
        {
            skill.X -= gameOffsetX;
            skill.Y -= gameOffsetY;
        }
        foreach (var effect in settings.Effects)
        {
            effect.NotificationX -= gameOffsetX;
            effect.NotificationY -= gameOffsetY;
        }
        foreach (var reminder in settings.Reminders)
        {
            reminder.X -= gameOffsetX;
            reminder.Y -= gameOffsetY;
        }
        foreach (var group in settings.BuffPanelGroups)
        {
            group.X -= gameOffsetX;
            group.Y -= gameOffsetY;
        }
        foreach (var item in settings.Equipment)
        {
            item.X -= gameOffsetX;
            item.Y -= gameOffsetY;
        }
        settings.TimersX -= gameOffsetX;
        settings.TimersY -= gameOffsetY;
        foreach (var group in settings.RewardGroups)
        {
            group.X -= gameOffsetX;
            group.Y -= gameOffsetY;
        }
        foreach (var indicator in settings.RateIndicators)
        {
            indicator.X -= gameOffsetX;
            indicator.Y -= gameOffsetY;
        }

        settings.OverlayCoordinatesRelativeToGame = true;
        return true;
    }
}
