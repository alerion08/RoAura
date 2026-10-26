using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly GoldGoblinService _goldGoblin;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Guid, DateTime> _cooldownEnds = [];
    private readonly List<MediaPlayer> _soundPlayers = [];

    private bool _skillEditMode;
    private bool _skillGroupMode;
    private SkillAura? _draggedSkill;
    private Point _skillDragStart;
    private readonly Dictionary<Guid, Point> _skillDragStarts = [];

    private bool _notificationEditMode;
    private bool _notificationGroupMode;
    private EffectNotification? _draggedNotification;
    private Point _notificationDragStart;
    private readonly Dictionary<Guid, Point> _notificationDragStarts = [];

    private bool _reminderEditMode;
    private bool _reminderGroupMode;
    private ReminderItem? _draggedReminder;
    private Point _reminderDragStart;
    private readonly Dictionary<Guid, Point> _reminderDragStarts = [];

    private bool _buffPanelEditMode;
    private BuffPanelGroup? _draggedBuffPanelGroup;
    private Point _buffPanelDragStart;
    private Point _buffPanelPositionStart;

    private bool _equipmentEditMode;
    private bool _equipmentGroupMode;
    private EquipmentTracker? _draggedEquipment;
    private Point _equipmentDragStart;
    private readonly Dictionary<Guid, Point> _equipmentDragStarts = [];

    private bool _timerPanelEditMode;
    private Point _timerPanelDragStart;
    private Point _timerPanelPositionStart;

    private bool _rewardGroupEditMode;
    private RewardGroupSettings? _draggedRewardGroup;
    private Point _rewardGroupDragStart;
    private Point _rewardGroupPositionStart;

    private bool _rateIndicatorEditMode;
    private bool _rateIndicatorGroupMode;
    private RateIndicatorSettings? _draggedRateIndicator;
    private Point _rateIndicatorDragStart;
    private readonly Dictionary<RateIndicatorKind, Point> _rateIndicatorDragStarts = [];
    private DateTime _lastRateIndicatorUpdateUtc = DateTime.MinValue;
    private long _sessionBaseExperience;
    private DateTime? _baseExperienceStartedUtc;
    private ulong _currentBaseExperience;
    private ulong _nextBaseExperience;
    private long _sessionJobExperience;
    private DateTime? _jobExperienceStartedUtc;
    private ulong _currentJobExperience;
    private ulong _nextJobExperience;
    private long _sessionZeny;
    private DateTime? _zenyStartedUtc;

    private bool _overlaysVisible = true;
    private ForegroundProcess.WindowBounds? _gameBounds;
    private Matrix _lastDeviceTransform = Matrix.Identity;
    private bool _gameWindowFound;
    private IntPtr _gameWindowHandle;
    private IntPtr _settingsWindowHandle;

    public bool IsBusyEditing => _skillEditMode || _notificationEditMode || _reminderEditMode ||
                                 _buffPanelEditMode || _equipmentEditMode || _timerPanelEditMode ||
                                 _rewardGroupEditMode || _rateIndicatorEditMode;
    public bool AreOverlaysVisible => _overlaysVisible;

    public void RefreshServerProfile()
    {
        var supported = GameServerProfile.SupportsServerDatabases(_settings.ServerProfile);
        RewardGroupsPanel.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        RateIndicatorsPanel.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        UpdateTimerPanelVisibility();
        if (supported) return;
        foreach (var group in _settings.RewardGroups) group.Notifications.Clear();
    }

    public OverlayWindow(AppSettings settings, SettingsStore store, GoldGoblinService goldGoblin)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;
        _goldGoblin = goldGoblin;
        _goldGoblin.Changed += () => Dispatcher.BeginInvoke(() => UpdateRateIndicators(true));
        DataContext = settings;
        _settings.SyncBuffPanelGroups();
        _settings.SyncRewardGroups();
        _settings.SyncRateIndicators();
        UpdateRateIndicators(true);

        Left = 0;
        Top = 0;
        Width = 1;
        Height = 1;

        SourceInitialized += (_, _) => ApplyWindowStyles();
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            // Ten updates per second are smooth enough for cooldown numbers and
            // avoid needlessly redrawing a full-screen transparent surface.
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += (_, _) =>
        {
            SyncOverlayToGameWindow();
            UpdateHostVisibility();
            UpdateCooldowns();
            UpdateRateIndicators();
        };
        _timer.Start();
        Loaded += (_, _) => SyncOverlayToGameWindow(true);
        UpdateTimerPanelVisibility();
        RefreshServerProfile();
    }

    public bool TriggerSkill(SkillAura skill, bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && _cooldownEnds.TryGetValue(skill.Id, out var currentEnd) && currentEnd > now)
            return false;

        _cooldownEnds[skill.Id] = now.AddSeconds(skill.CooldownSeconds);
        skill.IsReady = false;
        skill.RemainingSeconds = skill.CooldownSeconds;
        return true;
    }

    public void CompleteCooldown(SkillAura skill)
    {
        _cooldownEnds.Remove(skill.Id);
        skill.RemainingSeconds = 0;
        skill.IsReady = true;
    }

    public void SetSkillEditMode(bool enabled, bool groupMode)
    {
        _skillEditMode = enabled;
        _skillGroupMode = enabled && groupMode;
        _draggedSkill = null;
        _skillDragStarts.Clear();
        if (enabled)
            ShowEditBanner(groupMode
                ? LocalizationService.Get("OverlayMoveCooldownGroup")
                : LocalizationService.Get("OverlayMoveCooldownSingle"));
        else
            FinishEditingIfIdle();
    }

    public void SetNotificationEditMode(TrackedEffect? selectedEffect, bool enabled, bool groupMode)
    {
        RemoveNotificationPreviews();
        _notificationEditMode = enabled && selectedEffect is not null;
        _notificationGroupMode = _notificationEditMode && groupMode;
        _draggedNotification = null;
        _notificationDragStarts.Clear();

        if (!_notificationEditMode || selectedEffect is null)
        {
            FinishEditingIfIdle();
            return;
        }

        var effects = _notificationGroupMode && selectedEffect.GroupNumber > 0
            ? _settings.Effects.Where(effect => effect.Enabled && effect.GroupNumber == selectedEffect.GroupNumber)
            : [selectedEffect];

        foreach (var effect in effects)
            _settings.ActiveNotifications.Add(CreateNotification(effect, true, true));

        ShowEditBanner(groupMode
            ? LocalizationService.Get("OverlayMoveEffectGroup")
            : LocalizationService.Get("OverlayMoveEffectSingle"));
    }

    public void SetReminderEditMode(ReminderItem? selectedReminder, bool enabled, bool groupMode)
    {
        foreach (var reminder in _settings.Reminders)
            reminder.IsPlacementPreview = false;

        _reminderEditMode = enabled && selectedReminder is not null;
        _reminderGroupMode = _reminderEditMode && groupMode;
        _draggedReminder = null;
        _reminderDragStarts.Clear();

        if (!_reminderEditMode || selectedReminder is null)
        {
            FinishEditingIfIdle();
            return;
        }

        var reminders = _reminderGroupMode && selectedReminder.GroupNumber > 0
            ? _settings.Reminders.Where(reminder => reminder.Enabled && reminder.GroupNumber == selectedReminder.GroupNumber)
            : [selectedReminder];
        foreach (var reminder in reminders)
            reminder.IsPlacementPreview = true;

        ShowEditBanner(groupMode
            ? LocalizationService.Get("OverlayMoveReminderGroup")
            : LocalizationService.Get("OverlayMoveReminderSingle"));
    }

    public void SetBuffPanelEditMode(BuffPanelGroup? selectedGroup, bool enabled)
    {
        foreach (var group in _settings.BuffPanelGroups)
            group.IsPlacementPreview = false;

        _buffPanelEditMode = enabled && selectedGroup is not null;
        _draggedBuffPanelGroup = null;
        if (!_buffPanelEditMode || selectedGroup is null)
        {
            FinishEditingIfIdle();
            return;
        }

        selectedGroup.IsPlacementPreview = true;
        ShowEditBanner(LocalizationService.Get("OverlayMoveBuffPanelGroup"));
    }

    public void SetEquipmentEditMode(EquipmentTracker? selected, bool enabled, bool groupMode)
    {
        foreach (var item in _settings.Equipment) item.IsPlacementPreview = false;
        _equipmentEditMode = enabled && selected is not null;
        _equipmentGroupMode = _equipmentEditMode && groupMode;
        _draggedEquipment = null;
        _equipmentDragStarts.Clear();
        if (!_equipmentEditMode || selected is null) { FinishEditingIfIdle(); return; }
        var items = _equipmentGroupMode && selected.GroupNumber > 0
            ? _settings.Equipment.Where(item => item.Enabled && item.GroupNumber == selected.GroupNumber)
            : [selected];
        foreach (var item in items) item.IsPlacementPreview = true;
        ShowEditBanner(LocalizationService.Get(groupMode ? "OverlayMoveEquipmentGroup" : "OverlayMoveEquipmentSingle"));
    }

    public void SetTimerPanelEditMode(bool enabled)
    {
        _timerPanelEditMode = enabled;
        TimerPanelPreview.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled)
        {
            _settings.TimersOverlayVisible = true;
            ShowEditBanner(LocalizationService.Get("OverlayMoveTimers"));
        }
        else FinishEditingIfIdle();
        UpdateTimerPanelVisibility();
    }

    public void SetRewardGroupEditMode(RewardGroupSettings? selectedGroup, bool enabled)
    {
        foreach (var group in _settings.RewardGroups) group.IsPlacementPreview = false;
        _rewardGroupEditMode = enabled && selectedGroup is not null;
        _draggedRewardGroup = null;
        if (!_rewardGroupEditMode || selectedGroup is null)
        {
            FinishEditingIfIdle();
            return;
        }

        selectedGroup.IsPlacementPreview = true;
        ShowEditBanner(LocalizationService.Get("OverlayMoveRewardGroup"));
    }

    public void SetRateIndicatorEditMode(RateIndicatorSettings? selected, bool enabled, bool groupMode)
    {
        foreach (var indicator in _settings.RateIndicators) indicator.IsPlacementPreview = false;
        _rateIndicatorEditMode = enabled && selected is not null;
        _rateIndicatorGroupMode = _rateIndicatorEditMode && groupMode;
        _draggedRateIndicator = null;
        _rateIndicatorDragStarts.Clear();
        if (!_rateIndicatorEditMode || selected is null)
        {
            FinishEditingIfIdle();
            return;
        }

        var indicators = _rateIndicatorGroupMode && selected.GroupNumber > 0
            ? _settings.RateIndicators.Where(indicator => indicator.GroupNumber == selected.GroupNumber)
            : [selected];
        foreach (var indicator in indicators) indicator.IsPlacementPreview = true;
        ShowEditBanner(LocalizationService.Get(groupMode
            ? "OverlayMoveRateIndicatorGroup"
            : "OverlayMoveRateIndicator"));
    }

    public void EndAllEditModes()
    {
        _skillEditMode = false;
        _notificationEditMode = false;
        _reminderEditMode = false;
        _buffPanelEditMode = false;
        _equipmentEditMode = false;
        _timerPanelEditMode = false;
        _rewardGroupEditMode = false;
        _rateIndicatorEditMode = false;
        _rateIndicatorGroupMode = false;
        TimerPanelPreview.Visibility = Visibility.Collapsed;
        _draggedSkill = null;
        _draggedNotification = null;
        _draggedReminder = null;
        _draggedBuffPanelGroup = null;
        _draggedEquipment = null;
        _draggedRewardGroup = null;
        _draggedRateIndicator = null;
        _rateIndicatorDragStarts.Clear();
        _skillDragStarts.Clear();
        _notificationDragStarts.Clear();
        _reminderDragStarts.Clear();
        _equipmentDragStarts.Clear();
        RemoveNotificationPreviews();
        foreach (var reminder in _settings.Reminders)
            reminder.IsPlacementPreview = false;
        foreach (var group in _settings.BuffPanelGroups)
            group.IsPlacementPreview = false;
        foreach (var item in _settings.Equipment)
            item.IsPlacementPreview = false;
        foreach (var group in _settings.RewardGroups)
            group.IsPlacementPreview = false;
        foreach (var indicator in _settings.RateIndicators)
            indicator.IsPlacementPreview = false;
        UpdateTimerPanelVisibility();
        FinishEditingIfIdle();
    }

    public void SetOverlaysVisible(bool visible)
    {
        _overlaysVisible = visible;
        if (visible)
        {
            Show();
            BringOverlayAboveGame();
        }
        else Hide();
    }

    public void ShowEffectNotification(TrackedEffect effect, bool appeared)
    {
        var text = appeared ? effect.AppearedText : effect.DisappearedText;
        if (string.IsNullOrWhiteSpace(text))
            text = appeared ? $"{effect.Name}: appeared" : $"{effect.Name}: ended";

        foreach (var old in _settings.ActiveNotifications
                     .Where(notification => notification.EffectId == effect.Id && !notification.IsPlacementPreview)
                     .ToList())
            _settings.ActiveNotifications.Remove(old);

        var notification = CreateNotification(effect, appeared, false);
        _settings.ActiveNotifications.Add(notification);
        ReflowNotifications();
        if (_overlaysVisible)
        {
            Show();
            BringOverlayAboveGame();
        }
        PlayEffectSound(effect, appeared);
        _ = RemoveNotificationLaterAsync(notification, effect.NotificationDurationSeconds);
    }

    public void ShowLootReward(int itemId, string itemName, string itemType, int count) =>
        ShowReward(RewardKind.Loot, $"item:{itemId}", itemName, count, 0, itemType, itemId);

    public void ShowZenyReward(long amount) =>
        ShowReward(RewardKind.Zeny, "zeny", "Zeny", amount, 0);

    public void ShowExperienceReward(long amount, double percentage, bool jobExperience) =>
        ShowReward(RewardKind.Experience, jobExperience ? "job-exp" : "base-exp",
            jobExperience ? "Job XP" : "XP", amount, percentage);

    public void ShowRewardPreview(RewardGroupSettings group)
    {
        var channels = _settings.RewardChannels.Where(channel => channel.Enabled &&
            channel.GroupNumber == group.GroupNumber).OrderBy(channel => channel.Order).ToList();
        foreach (var channel in channels)
        {
            switch (channel.Kind)
            {
                case RewardKind.Loot: ShowReward(channel.Kind, $"preview-loot:{Guid.NewGuid()}", "Jellopy", 5, 0, "Loot"); break;
                case RewardKind.Zeny: ShowReward(channel.Kind, $"preview-zeny:{Guid.NewGuid()}", "Zeny", 2450, 0); break;
                case RewardKind.Experience: ShowReward(channel.Kind, $"preview-exp:{Guid.NewGuid()}", "XP", 15420, 0.37); break;
            }
        }
    }

    private void ShowReward(RewardKind kind, string aggregationKey, string label,
        long amount, double percentage, string? itemType = null, int itemId = 0)
    {
        if (!_settings.RewardsEnabled || amount <= 0 ||
            !GameServerProfile.SupportsServerDatabases(_settings.ServerProfile)) return;
        var channel = _settings.RewardChannel(kind);
        if (channel is null) return;
        _settings.SyncRewardGroups();
        var group = _settings.RewardGroups.FirstOrDefault(item => item.GroupNumber == channel.GroupNumber);
        if (group is null) return;

        var now = DateTime.UtcNow;
        var window = TimeSpan.FromMilliseconds(group.AggregationWindowMs);
        var notification = group.Notifications.FirstOrDefault(item =>
            item.Kind == kind && item.AggregationKey == aggregationKey &&
            now - item.LastUpdatedUtc <= window);
        if (notification is null)
        {
            notification = new RewardNotification
            {
                Kind = kind,
                AggregationKey = aggregationKey,
                ItemName = label,
                ItemId = itemId,
                Color = kind == RewardKind.Loot ? channel.ColorForItemType(itemType) : channel.TextColor,
                Amount = amount,
                Percentage = Math.Max(0, percentage),
                HasPercentage = percentage >= 0,
                LastUpdatedUtc = now,
                ExpiresAtUtc = now.AddSeconds(group.DurationSeconds)
            };
            group.Notifications.Add(notification);
        }
        else
        {
            notification.Amount += amount;
            if (percentage >= 0)
            {
                notification.Percentage += percentage;
                notification.HasPercentage = true;
            }
            notification.Color = kind == RewardKind.Loot ? channel.ColorForItemType(itemType) : channel.TextColor;
            notification.LastUpdatedUtc = now;
            notification.ExpiresAtUtc = now.AddSeconds(group.DurationSeconds);
        }

        notification.Text = FormatRewardText(notification);
        while (group.Notifications.Count > group.MaxLines)
            group.Notifications.Remove(group.Notifications.OrderBy(item => item.LastUpdatedUtc).First());
        SortRewardGroup(group);

        if (_overlaysVisible)
        {
            Show();
            BringOverlayAboveGame();
        }
    }

    public void RecordBaseExperience(long amount)
    {
        if (amount <= 0) return;
        _baseExperienceStartedUtc ??= DateTime.UtcNow;
        _sessionBaseExperience += amount;
        UpdateRateIndicators();
    }

    public void RecordJobExperience(long amount)
    {
        if (amount <= 0) return;
        _jobExperienceStartedUtc ??= DateTime.UtcNow;
        _sessionJobExperience += amount;
        UpdateRateIndicators();
    }

    public void SetCurrentBaseExperience(ulong value)
    {
        _currentBaseExperience = value;
        UpdateRateIndicators();
    }

    public void SetNextBaseExperience(ulong value)
    {
        _nextBaseExperience = value;
        UpdateRateIndicators();
    }

    public void SetCurrentJobExperience(ulong value)
    {
        _currentJobExperience = value;
        UpdateRateIndicators();
    }

    public void SetNextJobExperience(ulong value)
    {
        _nextJobExperience = value;
        UpdateRateIndicators();
    }

    public void RecordZeny(long amount)
    {
        if (amount <= 0) return;
        _zenyStartedUtc ??= DateTime.UtcNow;
        _sessionZeny += amount;
        UpdateRateIndicators();
    }

    public void ResetRateStatistics()
    {
        ResetRateSessionCounters();
        _currentBaseExperience = 0;
        _nextBaseExperience = 0;
        _currentJobExperience = 0;
        _nextJobExperience = 0;
        UpdateRateIndicators(true);
    }

    public void ResetRateSession()
    {
        ResetRateSessionCounters();
        UpdateRateIndicators(true);
    }

    private void ResetRateSessionCounters()
    {
        _sessionBaseExperience = 0;
        _baseExperienceStartedUtc = null;
        _sessionJobExperience = 0;
        _jobExperienceStartedUtc = null;
        _sessionZeny = 0;
        _zenyStartedUtc = null;
        _goldGoblin.Reset();
    }

    private void UpdateRateIndicators(bool force = false)
    {
        var now = DateTime.UtcNow;
        UpdateSessionTimeIndicator(now);
        if (!force && now - _lastRateIndicatorUpdateUtc <
            TimeSpan.FromSeconds(_settings.RateStatisticsRefreshSeconds))
            return;
        _lastRateIndicatorUpdateUtc = now;
        var xpPerHour = _baseExperienceStartedUtc.HasValue
            ? _sessionBaseExperience / Math.Max((now - _baseExperienceStartedUtc.Value).TotalHours, 1d / 3600d)
            : 0;
        var includeLootValue = _settings.ServerProfile == GameServerProfile.SpringRo && _settings.IncludeGoldGoblinInZenyPerHour;
        var effectiveZeny = _sessionZeny + (includeLootValue ? _goldGoblin.SessionTotal : 0);
        var zenyStarted = new[] { _zenyStartedUtc, includeLootValue ? _goldGoblin.SessionStartedUtc : null }
            .Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty().Min();
        var hasZenySession = _zenyStartedUtc.HasValue || (includeLootValue && _goldGoblin.SessionStartedUtc.HasValue);
        var zenyPerHour = hasZenySession
            ? effectiveZeny / Math.Max((now - zenyStarted).TotalHours, 1d / 3600d)
            : 0;
        var jobXpPerHour = _jobExperienceStartedUtc.HasValue
            ? _sessionJobExperience / Math.Max((now - _jobExperienceStartedUtc.Value).TotalHours, 1d / 3600d)
            : 0;
        var timeToLevel = "--:--:--";
        if (xpPerHour > 0 && _nextBaseExperience > _currentBaseExperience)
        {
            var seconds = (_nextBaseExperience - _currentBaseExperience) / xpPerHour * 3600d;
            if (seconds < TimeSpan.FromDays(1000).TotalSeconds)
            {
                var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
                timeToLevel = value.TotalDays >= 1
                    ? $"{(int)value.TotalDays}d {value:hh\\:mm\\:ss}"
                    : value.ToString("hh\\:mm\\:ss");
            }
        }
        var timeToJobLevel = "--:--:--";
        if (jobXpPerHour > 0 && _nextJobExperience > _currentJobExperience)
        {
            var seconds = (_nextJobExperience - _currentJobExperience) / jobXpPerHour * 3600d;
            if (seconds < TimeSpan.FromDays(1000).TotalSeconds)
            {
                var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
                timeToJobLevel = value.TotalDays >= 1
                    ? $"{(int)value.TotalDays}d {value:hh\\:mm\\:ss}"
                    : value.ToString("hh\\:mm\\:ss");
            }
        }

        var sessionStarted = new[]
            {
                _baseExperienceStartedUtc,
                _jobExperienceStartedUtc,
                _zenyStartedUtc,
                _settings.ServerProfile == GameServerProfile.SpringRo
                    ? _goldGoblin.SessionStartedUtc
                    : null
            }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Min();
        var hasSession = _baseExperienceStartedUtc.HasValue || _jobExperienceStartedUtc.HasValue ||
                         _zenyStartedUtc.HasValue ||
                         (_settings.ServerProfile == GameServerProfile.SpringRo &&
                          _goldGoblin.SessionStartedUtc.HasValue);
        var sessionTime = hasSession ? FormatElapsed(now - sessionStarted) : "00:00:00";

        foreach (var indicator in _settings.RateIndicators)
        {
            indicator.ValueText = indicator.Kind switch
            {
                RateIndicatorKind.XpPerHour => $"XP/h: {xpPerHour:N0}",
                RateIndicatorKind.TimeToLevel => _settings.Language == "en"
                    ? $"Time to level: {timeToLevel}"
                    : $"До уровня: {timeToLevel}",
                RateIndicatorKind.TimeToJobLevel => _settings.Language == "en"
                    ? $"Time to Job level: {timeToJobLevel}"
                    : $"До Job уровня: {timeToJobLevel}",
                RateIndicatorKind.ZenyPerHour => $"Zeny/h: {zenyPerHour:N0}",
                RateIndicatorKind.ZenyTotal => $"Zeny Total: {effectiveZeny:N0}",
                _ => _settings.Language == "en"
                    ? $"Session time: {sessionTime}"
                    : $"Время сессии: {sessionTime}"
            };
        }
    }

    private void UpdateSessionTimeIndicator(DateTime now)
    {
        var starts = new[]
            {
                _baseExperienceStartedUtc,
                _jobExperienceStartedUtc,
                _zenyStartedUtc,
                _settings.ServerProfile == GameServerProfile.SpringRo
                    ? _goldGoblin.SessionStartedUtc
                    : null
            }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
        var sessionTime = starts.Count > 0 ? FormatElapsed(now - starts.Min()) : "00:00:00";
        var indicator = _settings.RateIndicators.FirstOrDefault(item => item.Kind == RateIndicatorKind.SessionTime);
        if (indicator is null) return;
        indicator.ValueText = _settings.Language == "en"
            ? $"Session time: {sessionTime}"
            : $"Время сессии: {sessionTime}";
    }

    private static string FormatElapsed(TimeSpan value) => value.TotalDays >= 1
        ? $"{(int)value.TotalDays}d {value:hh\\:mm\\:ss}"
        : value.ToString("hh\\:mm\\:ss");

    private string FormatRewardText(RewardNotification notification) => notification.Kind switch
    {
        RewardKind.Loot => $"{notification.ItemName} x{notification.Amount:N0}" +
            (_settings.ServerProfile == GameServerProfile.SpringRo && _settings.ShowLootZenyPrice
                ? $" ({_goldGoblin.LootValue(notification.ItemId, notification.ItemName, notification.Amount):N0} Z)"
                : ""),
        RewardKind.Zeny => $"Zeny +{notification.Amount:N0}",
        _ => notification.HasPercentage
            ? $"{notification.ItemName} +{notification.Amount:N0} ({notification.Percentage:0.##}%)"
            : $"{notification.ItemName} +{notification.Amount:N0}"
    };

    private void SortRewardGroup(RewardGroupSettings group)
    {
        var order = _settings.RewardChannels.GroupBy(channel => channel.Kind)
            .ToDictionary(channels => channels.Key, channels => channels.First().Order);
        var sorted = group.Notifications.OrderBy(item => order.GetValueOrDefault(item.Kind, 99))
            .ThenBy(item => item.LastUpdatedUtc).ToList();
        if (sorted.SequenceEqual(group.Notifications)) return;
        group.Notifications.Clear();
        foreach (var item in sorted) group.Notifications.Add(item);
    }

    private EffectNotification CreateNotification(TrackedEffect effect, bool appeared, bool preview)
    {
        var text = appeared ? effect.AppearedText : effect.DisappearedText;
        if (string.IsNullOrWhiteSpace(text)) text = effect.Name;
        return new EffectNotification
        {
            EffectId = effect.Id,
            GroupNumber = effect.GroupNumber,
            Text = text,
            IconBase64 = effect.TemplateImageBase64,
            Color = effect.NotificationColor,
            BackgroundColor = effect.NotificationBackgroundColor,
            X = effect.NotificationX,
            Y = effect.NotificationY,
            BaseX = effect.NotificationX,
            BaseY = effect.NotificationY,
            IconSize = effect.NotificationIconSize,
            IconPadding = effect.NotificationIconPadding,
            TextSize = effect.NotificationTextSize,
            Opacity = effect.NotificationOpacityPercent / 100d,
            ShowIcon = effect.ShowNotificationIcon,
            ShowBackground = effect.ShowNotificationBackground,
            ShowBorder = effect.ShowNotificationBorder,
            IsPlacementPreview = preview
        };
    }

    private void RemoveNotificationPreviews()
    {
        foreach (var preview in _settings.ActiveNotifications.Where(item => item.IsPlacementPreview).ToList())
            _settings.ActiveNotifications.Remove(preview);
    }

    private async Task RemoveNotificationLaterAsync(EffectNotification notification, double durationSeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
        _settings.ActiveNotifications.Remove(notification);
        ReflowNotifications();
    }

    private void ReflowNotifications()
    {
        var active = _settings.ActiveNotifications.Where(notification => !notification.IsPlacementPreview);
        foreach (var group in active.GroupBy(notification => notification.GroupNumber > 0
                     ? $"group:{notification.GroupNumber}"
                     : $"effect:{notification.EffectId}"))
        {
            var first = group.First();
            var anchorEffect = first.GroupNumber > 0
                ? _settings.Effects.FirstOrDefault(effect => effect.GroupNumber == first.GroupNumber)
                : _settings.Effects.FirstOrDefault(effect => effect.Id == first.EffectId);
            var anchorX = anchorEffect?.NotificationX ?? first.BaseX;
            var anchorY = anchorEffect?.NotificationY ?? first.BaseY;
            var offset = 0d;
            foreach (var notification in group)
            {
                notification.X = anchorX;
                notification.Y = anchorY + offset;
                var iconHeight = notification.ShowIcon ? notification.IconSize : 0;
                offset += Math.Max(iconHeight, notification.TextSize * 1.5) + 20 + _settings.NotificationGap;
            }
        }
    }

    private void PlayEffectSound(TrackedEffect effect, bool appeared)
    {
        var enabled = appeared ? effect.AppearedSoundEnabled : effect.DisappearedSoundEnabled;
        var soundPath = appeared ? effect.AppearedSoundPath : effect.DisappearedSoundPath;
        var volume = appeared ? effect.AppearedSoundVolumePercent : effect.DisappearedSoundVolumePercent;
        PlayConfiguredSound(enabled, soundPath, volume);
    }

    public void PlayMvpTimerSound(MvpTimer timer) =>
        PlayConfiguredSound(timer.CompletionSoundEnabled, timer.CompletionSoundPath,
            timer.CompletionSoundVolumePercent);

    private void PlayConfiguredSound(bool enabled, string soundPath, double volume)
    {
        if (!enabled) return;
        if (string.IsNullOrWhiteSpace(soundPath) || !File.Exists(soundPath))
        {
            MessageBeep(0x00000040);
            return;
        }

        try
        {
            var player = new MediaPlayer { Volume = Math.Clamp(volume / 100d, 0, 1) };
            _soundPlayers.Add(player);
            void ReleasePlayer(object? sender, EventArgs args)
            {
                player.Close();
                _soundPlayers.Remove(player);
            }
            player.MediaEnded += ReleasePlayer;
            player.MediaFailed += (_, _) => ReleasePlayer(null, EventArgs.Empty);
            player.Open(new Uri(soundPath, UriKind.Absolute));
            player.Play();
        }
        catch
        {
            MessageBeep(0x00000040);
        }
    }

    private void UpdateCooldowns()
    {
        var now = DateTime.UtcNow;
        foreach (var skill in _settings.Skills)
        {
            if (!_cooldownEnds.TryGetValue(skill.Id, out var end)) continue;
            var remaining = (end - now).TotalSeconds;
            if (remaining <= 0) CompleteCooldown(skill);
            else skill.RemainingSeconds = remaining;
        }

        foreach (var effect in _settings.Effects.Where(effect => effect.IsActive && effect.DurationEndsAtUtc.HasValue))
        {
            var remaining = (effect.DurationEndsAtUtc!.Value - now).TotalSeconds;
            effect.RemainingSeconds = Math.Max(0, remaining);
            if (remaining <= 0) effect.DurationEndsAtUtc = null;
        }

        foreach (var timer in _settings.MvpTimers.Where(timer => timer.EndsAtUtc.HasValue))
        {
            var remaining = (timer.EndsAtUtc!.Value - now).TotalSeconds;
            timer.RemainingSeconds = Math.Max(0, remaining);
            if (remaining > 0) continue;

            timer.EndsAtUtc = null;
            if (!timer.CompletionSoundPlayed)
            {
                timer.CompletionSoundPlayed = true;
                PlayMvpTimerSound(timer);
            }
        }

        foreach (var group in _settings.RewardGroups)
        {
            foreach (var notification in group.Notifications
                         .Where(notification => notification.ExpiresAtUtc <= now).ToList())
                group.Notifications.Remove(notification);
        }

        UpdateRateIndicators();

        UpdateTimerPanelVisibility();
    }

    private void UpdateTimerPanelVisibility()
    {
        var hasRunningTimers = _settings.MvpTimers.Any(timer => timer.IsVisible);
        MvpTimerPanel.Visibility = _settings.TimersOverlayVisible &&
                                   GameServerProfile.SupportsServerDatabases(_settings.ServerProfile) &&
                                   (_timerPanelEditMode || hasRunningTimers)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Aura_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_skillEditMode || sender is not Border border || border.DataContext is not SkillAura skill)
            return;
        _draggedSkill = skill;
        _skillDragStart = e.GetPosition(this);
        _skillDragStarts.Clear();
        var items = _skillGroupMode && skill.GroupNumber > 0
            ? _settings.Skills.Where(item => item.GroupNumber == skill.GroupNumber)
            : [skill];
        foreach (var item in items)
            _skillDragStarts[item.Id] = new Point(item.X, item.Y);
        border.CaptureMouse();
        e.Handled = true;
    }

    private void Aura_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedSkill is null || e.LeftButton != MouseButtonState.Pressed) return;
        var mouse = e.GetPosition(this);
        var delta = ClampSkillDelta(mouse.X - _skillDragStart.X, mouse.Y - _skillDragStart.Y);
        foreach (var skill in _settings.Skills)
        {
            if (!_skillDragStarts.TryGetValue(skill.Id, out var start)) continue;
            skill.X = start.X + delta.X;
            skill.Y = start.Y + delta.Y;
        }
    }

    private Point ClampSkillDelta(double requestedX, double requestedY)
    {
        var moving = _settings.Skills.Where(skill => _skillDragStarts.ContainsKey(skill.Id)).ToList();
        if (moving.Count == 0) return new Point();
        var minX = moving.Min(skill => _skillDragStarts[skill.Id].X);
        var minY = moving.Min(skill => _skillDragStarts[skill.Id].Y);
        var maxX = moving.Max(skill => _skillDragStarts[skill.Id].X + skill.IndividualIconSize);
        var maxY = moving.Max(skill => _skillDragStarts[skill.Id].Y + skill.IndividualIconSize);
        return new Point(
            Math.Clamp(requestedX, -minX, Math.Max(-minX, ActualWidth - maxX)),
            Math.Clamp(requestedY, -minY, Math.Max(-minY, ActualHeight - maxY)));
    }

    private void Aura_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedSkill is null) return;
        if (sender is Border border) border.ReleaseMouseCapture();
        _draggedSkill = null;
        _skillDragStarts.Clear();
        _store.Save(_settings);
        e.Handled = true;
    }

    private void Notification_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_notificationEditMode || sender is not Border border ||
            border.DataContext is not EffectNotification notification || !notification.IsPlacementPreview)
            return;

        _draggedNotification = notification;
        _notificationDragStart = e.GetPosition(this);
        _notificationDragStarts.Clear();
        var selected = _settings.Effects.First(effect => effect.Id == notification.EffectId);
        var effects = _notificationGroupMode && selected.GroupNumber > 0
            ? _settings.Effects.Where(effect => effect.Enabled && effect.GroupNumber == selected.GroupNumber)
            : [selected];
        foreach (var effect in effects)
            _notificationDragStarts[effect.Id] = new Point(effect.NotificationX, effect.NotificationY);
        border.CaptureMouse();
        e.Handled = true;
    }

    private void Notification_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedNotification is null || e.LeftButton != MouseButtonState.Pressed) return;
        var mouse = e.GetPosition(this);
        var deltaX = mouse.X - _notificationDragStart.X;
        var deltaY = mouse.Y - _notificationDragStart.Y;
        foreach (var effect in _settings.Effects)
        {
            if (!_notificationDragStarts.TryGetValue(effect.Id, out var start)) continue;
            effect.NotificationX = Math.Clamp(start.X + deltaX, 0, ActualWidth);
            effect.NotificationY = Math.Clamp(start.Y + deltaY, 0, ActualHeight);
            var preview = _settings.ActiveNotifications.FirstOrDefault(item =>
                item.IsPlacementPreview && item.EffectId == effect.Id);
            if (preview is null) continue;
            preview.X = effect.NotificationX;
            preview.Y = effect.NotificationY;
        }
    }

    private void Notification_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedNotification is null) return;
        if (sender is Border border) border.ReleaseMouseCapture();
        _draggedNotification = null;
        _notificationDragStarts.Clear();
        _store.Save(_settings);
        e.Handled = true;
    }

    private void Reminder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_reminderEditMode || sender is not Border border || border.DataContext is not ReminderItem reminder)
            return;
        _draggedReminder = reminder;
        _reminderDragStart = e.GetPosition(this);
        _reminderDragStarts.Clear();
        var reminders = _reminderGroupMode && reminder.GroupNumber > 0
            ? _settings.Reminders.Where(item => item.Enabled && item.GroupNumber == reminder.GroupNumber)
            : [reminder];
        foreach (var item in reminders)
            _reminderDragStarts[item.Id] = new Point(item.X, item.Y);
        border.CaptureMouse();
        e.Handled = true;
    }

    private void Reminder_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedReminder is null || e.LeftButton != MouseButtonState.Pressed) return;
        var mouse = e.GetPosition(this);
        var requestedX = mouse.X - _reminderDragStart.X;
        var requestedY = mouse.Y - _reminderDragStart.Y;
        var moving = _settings.Reminders.Where(item => _reminderDragStarts.ContainsKey(item.Id)).ToList();
        foreach (var reminder in moving)
        {
            var start = _reminderDragStarts[reminder.Id];
            reminder.X = Math.Clamp(start.X + requestedX, 0, ActualWidth);
            reminder.Y = Math.Clamp(start.Y + requestedY, 0, ActualHeight);
        }
    }

    private void Reminder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedReminder is null) return;
        if (sender is Border border) border.ReleaseMouseCapture();
        _draggedReminder = null;
        _reminderDragStarts.Clear();
        _store.Save(_settings);
        e.Handled = true;
    }

    private void BuffPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_buffPanelEditMode || sender is not Border border ||
            border.DataContext is not BuffPanelGroup group || !group.IsPlacementPreview)
            return;
        _draggedBuffPanelGroup = group;
        _buffPanelDragStart = e.GetPosition(this);
        _buffPanelPositionStart = new Point(group.X, group.Y);
        border.CaptureMouse();
        e.Handled = true;
    }

    private void BuffPanel_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedBuffPanelGroup is null || e.LeftButton != MouseButtonState.Pressed) return;
        var mouse = e.GetPosition(this);
        _draggedBuffPanelGroup.X = Math.Clamp(_buffPanelPositionStart.X + mouse.X - _buffPanelDragStart.X, 0, ActualWidth);
        _draggedBuffPanelGroup.Y = Math.Clamp(_buffPanelPositionStart.Y + mouse.Y - _buffPanelDragStart.Y, 0, ActualHeight);
    }

    private void BuffPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedBuffPanelGroup is null) return;
        if (sender is Border border) border.ReleaseMouseCapture();
        _draggedBuffPanelGroup = null;
        _store.Save(_settings);
        e.Handled = true;
    }

    private void Equipment_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_equipmentEditMode || sender is not Border border || border.DataContext is not EquipmentTracker item) return;
        _draggedEquipment = item;
        _equipmentDragStart = e.GetPosition(this);
        _equipmentDragStarts.Clear();
        var items = _equipmentGroupMode && item.GroupNumber > 0
            ? _settings.Equipment.Where(other => other.Enabled && other.GroupNumber == item.GroupNumber)
            : [item];
        foreach (var other in items) _equipmentDragStarts[other.Id] = new Point(other.X, other.Y);
        border.CaptureMouse();
        e.Handled = true;
    }

    private void Equipment_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedEquipment is null || e.LeftButton != MouseButtonState.Pressed) return;
        var mouse = e.GetPosition(this);
        var dx = mouse.X - _equipmentDragStart.X;
        var dy = mouse.Y - _equipmentDragStart.Y;
        foreach (var item in _settings.Equipment)
        {
            if (!_equipmentDragStarts.TryGetValue(item.Id, out var start)) continue;
            item.X = Math.Clamp(start.X + dx, 0, ActualWidth);
            item.Y = Math.Clamp(start.Y + dy, 0, ActualHeight);
        }
    }

    private void Equipment_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedEquipment is null) return;
        if (sender is Border border) border.ReleaseMouseCapture();
        _draggedEquipment = null;
        _equipmentDragStarts.Clear();
        _store.Save(_settings);
        e.Handled = true;
    }

    private void MvpTimerPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_timerPanelEditMode || sender is not Border border) return;
        _timerPanelDragStart = e.GetPosition(this);
        _timerPanelPositionStart = new Point(_settings.TimersX, _settings.TimersY);
        border.CaptureMouse();
        e.Handled = true;
    }

    private void MvpTimerPanel_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_timerPanelEditMode || e.LeftButton != MouseButtonState.Pressed) return;
        var mouse = e.GetPosition(this);
        _settings.TimersX = Math.Clamp(_timerPanelPositionStart.X + mouse.X - _timerPanelDragStart.X, 0, ActualWidth);
        _settings.TimersY = Math.Clamp(_timerPanelPositionStart.Y + mouse.Y - _timerPanelDragStart.Y, 0, ActualHeight);
    }

    private void MvpTimerPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_timerPanelEditMode) return;
        if (sender is Border border) border.ReleaseMouseCapture();
        _store.Save(_settings);
        e.Handled = true;
    }

    private void RewardGroup_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_rewardGroupEditMode || sender is not Border border ||
            border.DataContext is not RewardGroupSettings group || !group.IsPlacementPreview)
            return;
        _draggedRewardGroup = group;
        _rewardGroupDragStart = e.GetPosition(this);
        _rewardGroupPositionStart = new Point(group.X, group.Y);
        border.CaptureMouse();
        e.Handled = true;
    }

    private void RewardGroup_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedRewardGroup is null || e.LeftButton != MouseButtonState.Pressed) return;
        var mouse = e.GetPosition(this);
        _draggedRewardGroup.X = Math.Clamp(_rewardGroupPositionStart.X + mouse.X - _rewardGroupDragStart.X, 0, ActualWidth);
        _draggedRewardGroup.Y = Math.Clamp(_rewardGroupPositionStart.Y + mouse.Y - _rewardGroupDragStart.Y, 0, ActualHeight);
    }

    private void RewardGroup_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedRewardGroup is null) return;
        if (sender is Border border) border.ReleaseMouseCapture();
        _draggedRewardGroup = null;
        _store.Save(_settings);
        e.Handled = true;
    }

    private void RateIndicator_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_rateIndicatorEditMode || sender is not Border border ||
            border.DataContext is not RateIndicatorSettings indicator || !indicator.IsPlacementPreview)
            return;
        _draggedRateIndicator = indicator;
        _rateIndicatorDragStart = e.GetPosition(this);
        _rateIndicatorDragStarts.Clear();
        var indicators = _rateIndicatorGroupMode && indicator.GroupNumber > 0
            ? _settings.RateIndicators.Where(item => item.GroupNumber == indicator.GroupNumber)
            : [indicator];
        foreach (var item in indicators)
            _rateIndicatorDragStarts[item.Kind] = new Point(item.X, item.Y);
        border.CaptureMouse();
        e.Handled = true;
    }

    private void RateIndicator_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedRateIndicator is null || e.LeftButton != MouseButtonState.Pressed) return;
        var mouse = e.GetPosition(this);
        var dx = mouse.X - _rateIndicatorDragStart.X;
        var dy = mouse.Y - _rateIndicatorDragStart.Y;
        foreach (var indicator in _settings.RateIndicators)
        {
            if (!_rateIndicatorDragStarts.TryGetValue(indicator.Kind, out var start)) continue;
            indicator.X = Math.Clamp(start.X + dx, 0, ActualWidth);
            indicator.Y = Math.Clamp(start.Y + dy, 0, ActualHeight);
        }
    }

    private void RateIndicator_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedRateIndicator is null) return;
        if (sender is Border border) border.ReleaseMouseCapture();
        _draggedRateIndicator = null;
        _rateIndicatorDragStarts.Clear();
        _store.Save(_settings);
        e.Handled = true;
    }

    private void ShowEditBanner(string text)
    {
        SyncOverlayToGameWindow(true);
        EditBannerText.Text = text.Replace("ESC", _settings.FinishMoveHotkey, StringComparison.OrdinalIgnoreCase);
        PositionEditBanner();
        EditBanner.Visibility = Visibility.Visible;
        _overlaysVisible = true;
        ApplyWindowStyles();
        Show();
        Activate();
    }

    private void FinishEditingIfIdle()
    {
        if (IsBusyEditing) return;
        EditBanner.Visibility = Visibility.Collapsed;
        ApplyWindowStyles();
        _store.Save(_settings);
    }

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLong(hwnd, GwlExStyle) | WsExToolWindow;
        if (IsBusyEditing) style &= ~(WsExTransparent | WsExNoActivate);
        else style |= WsExTransparent | WsExNoActivate;
        SetWindowLong(hwnd, GwlExStyle, style);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private void BringOverlayAboveGame()
    {
        if (!IsVisible && _gameWindowFound) Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _gameWindowHandle == IntPtr.Zero) return;
        // Protected/fullscreen RO clients can occupy the topmost band. Raise
        // the click-through overlay into that band only while RO is foreground.
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void ReleaseOverlayTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HwndNotTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void PositionEditBanner()
    {
        EditBanner.Margin = new Thickness(
            Math.Clamp(24, 8, Math.Max(8, ActualWidth - 620)),
            Math.Clamp(20, 8, Math.Max(8, ActualHeight - 70)), 0, 0);
    }

    private void SyncOverlayToGameWindow(bool force = false)
    {
        _gameWindowFound = ForegroundProcess.TryFindWindow(_settings.AllowedProcesses,
            out var gameHandle, out var gameBounds);
        if (!_gameWindowFound)
        {
            _gameBounds = null;
            _gameWindowHandle = IntPtr.Zero;
            return;
        }
        ForegroundProcess.WindowBounds? bounds = gameBounds;

        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        if (!force && _gameBounds == bounds && _lastDeviceTransform == fromDevice) return;
        var topLeft = fromDevice.Transform(new Point(bounds.Value.Left, bounds.Value.Top));
        var bottomRight = fromDevice.Transform(new Point(bounds.Value.Right, bounds.Value.Bottom));
        var width = Math.Max(1, bottomRight.X - topLeft.X);
        var height = Math.Max(1, bottomRight.Y - topLeft.Y);

        if (_gameWindowHandle != gameHandle)
            _gameWindowHandle = gameHandle;

        if (OverlayCoordinateMigration.Apply(_settings,
                topLeft.X - SystemParameters.VirtualScreenLeft,
                topLeft.Y - SystemParameters.VirtualScreenTop))
        {
            _store.Save(_settings);
        }

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = width;
        Height = height;
        _gameBounds = bounds;
        _lastDeviceTransform = fromDevice;
        BringOverlayAboveGame();
        if (EditBanner.Visibility == Visibility.Visible) PositionEditBanner();
    }

    private void UpdateHostVisibility()
    {
        if (!_overlaysVisible)
        {
            if (IsVisible) Hide();
            return;
        }

        var shouldShow = _gameWindowFound;
        if (shouldShow)
        {
            if (ForegroundProcess.IsForegroundAllowed(_settings.AllowedProcesses))
            {
                if (!IsVisible) Show();
                BringOverlayAboveGame();
            }
            else if (IsSettingsWindowForeground())
            {
                ReleaseOverlayTopmost();
                if (IsVisible) Hide();
            }
            else if (_gameBounds.HasValue && ForegroundProcess.DoesForegroundWindowCover(_gameBounds.Value))
            {
                ReleaseOverlayTopmost();
                if (IsVisible) Hide();
            }
            else
            {
                if (!IsVisible) Show();
                ReleaseOverlayTopmost();
            }
        }
        else if (IsVisible)
            Hide();
    }

    private bool IsSettingsWindowForeground()
    {
        if (_settingsWindowHandle == IntPtr.Zero)
        {
            var settingsWindow = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
            if (settingsWindow is not null)
                _settingsWindowHandle = new WindowInteropHelper(settingsWindow).Handle;
        }
        return ForegroundProcess.IsForegroundWindow(_settingsWindowHandle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);
}
