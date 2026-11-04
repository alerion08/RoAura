using System.Windows;
using RoAura.Services;

namespace RoAura;

public partial class OverlayWindow
{
    public bool IsLayoutEditing { get; private set; }
    private bool _layoutTimerPreview;
    internal void SetLayoutPreview(bool enabled, IReadOnlyList<OverlayLayoutTarget> targets)
    {
        IsLayoutEditing = enabled;
        _layoutTimerPreview = enabled && targets.Any(target => target.Key == "mvp");
        var keys = targets.Select(target => target.Key).ToHashSet();
        foreach (var skill in _settings.Skills)
            skill.IsPlacementPreview = enabled && skill.Enabled && skill.CharacterScope.IsAllowed;
        foreach (var reminder in _settings.Reminders)
            reminder.IsPlacementPreview = enabled && reminder.Enabled && keys.Contains($"reminder:{reminder.GroupNumber}") && reminder.CharacterScope.IsAllowed;
        foreach (var item in _settings.Equipment)
            item.IsPlacementPreview = enabled && item.Enabled && keys.Contains($"equipment:{item.GroupNumber}") && item.CharacterScope.IsAllowed;
        foreach (var group in _settings.BuffPanelGroups)
            group.IsPlacementPreview = enabled && keys.Contains($"buff:{group.GroupNumber}");
        foreach (var group in _settings.RewardGroups)
            group.IsPlacementPreview = enabled && keys.Contains($"reward:{group.GroupNumber}");
        RemoveNotificationPreviews();
        if (enabled)
            foreach (var effect in _settings.Effects.Where(effect => effect.Enabled && effect.CharacterScope.IsAllowed && keys.Contains($"effect:{effect.GroupNumber}")))
                _settings.ActiveNotifications.Add(CreateNotification(effect, true, true));
        foreach (var notification in _settings.ActiveMarketNotifications.Where(row => row.IsPlacementPreview).ToArray())
            _settings.ActiveMarketNotifications.Remove(notification);
        if (enabled && keys.Contains("market"))
            ShowMarketWatchNotification(new RoAura.Models.MarketWatchMatch(730, "1carat Diamond", 5000), preview: true, placement: true);
        TimerPanelPreview.Visibility = _layoutTimerPreview || _timerPanelEditMode ? Visibility.Visible : Visibility.Collapsed;
        if (enabled) HideMvpMap();
        UpdateTimerPanelVisibility(); UpdateTimerPanelPreviewPosition();
    }
    internal void RefreshLayoutPreviewPositions()
    {
        foreach (var preview in _settings.ActiveNotifications.Where(row => row.IsPlacementPreview))
        {
            var effect = _settings.Effects.FirstOrDefault(row => row.Id == preview.EffectId);
            if (effect is null) continue;
            preview.X = effect.NotificationX; preview.Y = effect.NotificationY;
        }
        ReflowNotifications(); UpdateTimerPanelPreviewPosition();
    }
}
