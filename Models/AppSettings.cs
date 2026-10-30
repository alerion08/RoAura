using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class AppSettings : INotifyPropertyChanged
{
    private bool _showShortNames = true;
    private bool _showReadyText = true;
    private double _iconSize = 72;
    private double _iconOpacity = 1;
    private bool _packetCaptureEnabled = true;
    private bool _autoDetectConnection = true;
    private string _packetServerAddress = "";
    private int _packetServerPort = 1;
    private uint _packetPlayerAid;
    private string _packetCaptureStateText = "Пакеты: запуск…";
    private string _packetCaptureStateKind = "Searching";
    private double _notificationX = 900;
    private double _notificationY = 250;
    private double _notificationSize = 72;
    private double _notificationGap = 10;
    private string _language = "ru";
    private string _settingsHotkey = "F8";
    private string _overlaysHotkey = "F9";
    private string _finishMoveHotkey = "Escape";
    private string _timersHotkey = "F10";
    private string _databaseHotkey = "F7";
    private string _questBrowserHotkey = "F6";
    private string _questTrackerHotkey = "F5";
    private double _marketBackgroundRefreshMinutes = 10;
    private int _marketWatchHistoryRetentionDays = 30;
    private bool _timersOverlayVisible = true;
    private double _timersX = 1180;
    private double _timersY = 180;
    private double _timersTextSize = 24;
    private double _timersGap = 6;
    private string _timersTextColor = "#FFFFFFFF";
    private bool _rewardsEnabled = true;
    private bool _showJobExperience;
    private double _rateStatisticsRefreshSeconds = 5;
    private string _lootDatabaseVersion = GameDatabaseVersion.Rtm;
    private string _mvpDatabaseVersion = GameDatabaseVersion.Rtm;
    private string _serverProfile = GameServerProfile.Rtm;
    private int _goldGoblinOverchargeLevel;
    private bool _showLootZenyPrice;
    private bool _includeGoldGoblinInZenyPerHour;

    public int SettingsVersion { get; set; }
    public bool OverlayCoordinatesRelativeToGame { get; set; }
    public string ServerProfile
    {
        get => _serverProfile;
        set
        {
            var normalized = GameServerProfile.Normalize(value);
            if (_serverProfile != normalized)
            {
                Set(ref _serverProfile, normalized);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SupportsMarket)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveMvpTimers)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveGoldGoblinPriceRules)));
            }
        }
    }

    [JsonIgnore] public bool SupportsMarket => GameServerProfile.SupportsMarket(ServerProfile);

    private bool _enabled = true;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool OnlyWhenGameIsActive { get; set; } = true;
    public bool ShowShortNames { get => _showShortNames; set => Set(ref _showShortNames, value); }
    public bool ShowReadyText { get => _showReadyText; set => Set(ref _showReadyText, value); }
    public double IconSize { get => _iconSize; set => Set(ref _iconSize, Math.Clamp(value, 40, 128)); }
    public double IconOpacity { get => _iconOpacity; set => Set(ref _iconOpacity, Math.Clamp(value, 0.2, 1)); }
    public bool PacketCaptureEnabled { get => _packetCaptureEnabled; set => Set(ref _packetCaptureEnabled, value); }
    public bool AutoDetectConnection { get => _autoDetectConnection; set => Set(ref _autoDetectConnection, value); }
    public string PacketServerAddress { get => _packetServerAddress; set => Set(ref _packetServerAddress, value?.Trim() ?? ""); }
    public int PacketServerPort { get => _packetServerPort; set => Set(ref _packetServerPort, Math.Clamp(value, 1, 65535)); }
    public uint PacketPlayerAid { get => _packetPlayerAid; set => Set(ref _packetPlayerAid, value); }
    public double NotificationX { get => _notificationX; set => Set(ref _notificationX, value); }
    public double NotificationY { get => _notificationY; set => Set(ref _notificationY, value); }
    public double NotificationSize { get => _notificationSize; set => Set(ref _notificationSize, Math.Clamp(value, 40, 180)); }
    public double NotificationGap { get => _notificationGap; set => Set(ref _notificationGap, Math.Clamp(value, 0, 40)); }
    public string Language { get => _language; set => Set(ref _language, value is "en" ? "en" : "ru"); }
    public string SettingsHotkey { get => _settingsHotkey; set => Set(ref _settingsHotkey, NormalizeHotkey(value, "F8")); }
    public string OverlaysHotkey { get => _overlaysHotkey; set => Set(ref _overlaysHotkey, NormalizeHotkey(value, "F9")); }
    public string FinishMoveHotkey { get => _finishMoveHotkey; set => Set(ref _finishMoveHotkey, NormalizeHotkey(value, "Escape")); }
    public string TimersHotkey { get => _timersHotkey; set => Set(ref _timersHotkey, NormalizeHotkey(value, "F10")); }
    public string DatabaseHotkey { get => _databaseHotkey; set => Set(ref _databaseHotkey, NormalizeHotkey(value, "F7")); }
    public string QuestBrowserHotkey { get => _questBrowserHotkey; set => Set(ref _questBrowserHotkey, NormalizeHotkey(value,"F6")); }
    public string QuestTrackerHotkey { get => _questTrackerHotkey; set => Set(ref _questTrackerHotkey, NormalizeHotkey(value,"F5")); }
    [JsonIgnore] public bool QuestHotkeyConflict { get { var enabled=Services.QuestHotkeys.Enabled(this); return !enabled.Browser || !enabled.Tracker; } }
    public int MarketWatchHistoryRetentionDays
    {
        get => _marketWatchHistoryRetentionDays;
        set => Set(ref _marketWatchHistoryRetentionDays, Math.Clamp(value, 1, 3650));
    }
    public double MarketBackgroundRefreshMinutes
    {
        get => _marketBackgroundRefreshMinutes;
        set => Set(ref _marketBackgroundRefreshMinutes, double.IsFinite(value) ? Math.Clamp(value, 1, 1440) : 10);
    }
    public bool TimersOverlayVisible { get => _timersOverlayVisible; set => Set(ref _timersOverlayVisible, value); }
    public double TimersX { get => _timersX; set => Set(ref _timersX, value); }
    public double TimersY { get => _timersY; set => Set(ref _timersY, value); }
    public double TimersTextSize { get => _timersTextSize; set => Set(ref _timersTextSize, Math.Clamp(value, 10, 72)); }
    public double TimersGap { get => _timersGap; set => Set(ref _timersGap, Math.Clamp(value, 0, 40)); }
    public string TimersTextColor { get => _timersTextColor; set => Set(ref _timersTextColor, value); }
    public bool RewardsEnabled { get => _rewardsEnabled; set => Set(ref _rewardsEnabled, value); }
    public bool ShowJobExperience { get => _showJobExperience; set => Set(ref _showJobExperience, value); }
    public int GoldGoblinOverchargeLevel { get => _goldGoblinOverchargeLevel; set => Set(ref _goldGoblinOverchargeLevel, Math.Clamp(value, 0, 10)); }
    public bool ShowLootZenyPrice { get => _showLootZenyPrice; set => Set(ref _showLootZenyPrice, value); }
    public bool IncludeGoldGoblinInZenyPerHour { get => _includeGoldGoblinInZenyPerHour; set => Set(ref _includeGoldGoblinInZenyPerHour, value); }
    public double RateStatisticsRefreshSeconds
    {
        get => _rateStatisticsRefreshSeconds;
        set => Set(ref _rateStatisticsRefreshSeconds, Math.Clamp(value, 1, 60));
    }
    public string LootDatabaseVersion
    {
        get => _lootDatabaseVersion;
        set => Set(ref _lootDatabaseVersion, GameDatabaseVersion.Normalize(value));
    }
    public string MvpDatabaseVersion
    {
        get => _mvpDatabaseVersion;
        set => Set(ref _mvpDatabaseVersion, GameDatabaseVersion.Normalize(value));
    }

    [JsonIgnore]
    public IReadOnlyList<string> GameDatabaseVersions { get; } =
        [GameDatabaseVersion.Rtm, GameDatabaseVersion.SpringRo, GameDatabaseVersion.Toc];

    [JsonIgnore]
    public string PacketCaptureStateText { get => _packetCaptureStateText; set => Set(ref _packetCaptureStateText, value); }
    [JsonIgnore]
    public string PacketCaptureStateKind { get => _packetCaptureStateKind; private set => Set(ref _packetCaptureStateKind, value); }

    public void UpdatePacketCaptureState(string state)
    {
        PacketCaptureStateText = state;
        var normalized = state.ToLowerInvariant();
        var error = normalized.Contains("ошибка") || normalized.Contains("error") ||
                    normalized.Contains("не установлен") || normalized.Contains("not installed") ||
                    normalized.Contains("не нашёл") || normalized.Contains("found no") ||
                    normalized.Contains("не удалось") || normalized.Contains("could not") ||
                    normalized.Contains("разрядност") || normalized.Contains("architecture") ||
                    normalized.Contains("только в windows") || normalized.Contains("windows only") || normalized.Contains("only on windows");
        PacketCaptureStateKind = error
            ? "Error"
            : normalized.Contains("слушаю") || normalized.Contains("listening")
            ? "Ready"
            : normalized.Contains("подключ") || normalized.Contains("connecting") || normalized.Contains("запуск") || normalized.Contains("restarting")
                ? "Searching"
                : normalized.Contains("отключ") || normalized.Contains("disabled")
                    ? "Warning"
                    : "Error";
    }

    public void RefreshPacketCaptureStateLanguage()
    {
        PacketCaptureStateText = PacketCaptureStateKind switch
        {
            "Ready" => Language == "en" ? "Packets: capture is active" : "Пакеты: захват работает",
            "Searching" => Language == "en" ? "Packets: connecting to Npcap…" : "Пакеты: подключение к Npcap…",
            "Warning" => Language == "en" ? "Packets: disabled" : "Пакеты: отключены",
            _ => Language == "en" ? "Packets: connection error" : "Пакеты: ошибка подключения"
        };
    }
    private List<string> _allowedProcesses = ["PRM", "Refuge", "ragexe", "RagexeRE"];
    public List<string> AllowedProcesses
    {
        get => _allowedProcesses;
        set
        {
            var incoming = value ?? [];
            if (_allowedProcesses.SequenceEqual(incoming, StringComparer.OrdinalIgnoreCase)) return;
            Set(ref _allowedProcesses, incoming);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowedProcessesText)));
        }
    }
    [JsonIgnore] public string AllowedProcessesText
    {
        get => string.Join(", ", AllowedProcesses);
        set => AllowedProcesses = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public ObservableCollection<CharacterProfile> Characters { get; set; } = [];
    public QuestOverlaySettings QuestOverlay { get; set; } = new();
    public ObservableCollection<NavigationPreference> NavigationSections { get; set; } = new(
        Enum.GetValues<AppFeature>().Where(f => f is not (AppFeature.Home or AppFeature.Settings))
            .Select(f => new NavigationPreference { Feature = f }));
    public void EnsureNavigationSections()
    {
        foreach (var feature in Enum.GetValues<AppFeature>().Where(f => f is not (AppFeature.Home or AppFeature.Settings)))
            if (!NavigationSections.Any(p => p.Feature == feature)) NavigationSections.Add(new NavigationPreference { Feature = feature });
    }
    public bool IsNavigationVisible(AppFeature feature) => feature is AppFeature.Home or AppFeature.Settings || NavigationSections.FirstOrDefault(p => p.Feature == feature)?.Visible != false;
    public ObservableCollection<SkillAura> Skills { get; set; } = [];
    public ObservableCollection<CooldownGroupSettings> CooldownGroups { get; set; } = [];

    public ObservableCollection<TrackedEffect> Effects { get; set; } = [];

    public ObservableCollection<ReminderItem> Reminders { get; set; } = [];
    public ObservableCollection<EquipmentTracker> Equipment { get; set; } = [];
    public ObservableCollection<MvpTimer> TocMvpTimers { get; set; } = [];
    [JsonIgnore] public ObservableCollection<MvpTimer> ActiveMvpTimers => ServerProfile == GameServerProfile.Toc ? TocMvpTimers : MvpTimers;
    public ObservableCollection<GoldGoblinPriceRule> TocGoldGoblinPriceRules { get; set; } = [];
    [JsonIgnore] public ObservableCollection<GoldGoblinPriceRule> ActiveGoldGoblinPriceRules => ServerProfile == GameServerProfile.Toc ? TocGoldGoblinPriceRules : GoldGoblinPriceRules;
    public ObservableCollection<MvpTimer> MvpTimers { get; set; } = [];
    public ObservableCollection<BuffPanelGroup> BuffPanelGroups { get; set; } = [];
    public ObservableCollection<RewardChannelSettings> RewardChannels { get; set; } =
    [
        new() { Kind = RewardKind.Loot, GroupNumber = 1, Order = 1, TextColor = "#FFFFD45C" },
        new() { Kind = RewardKind.Zeny, GroupNumber = 1, Order = 2, TextColor = "#FFFFE16A" },
        new() { Kind = RewardKind.Experience, GroupNumber = 1, Order = 3, TextColor = "#FF6FD8FF" }
    ];
    public ObservableCollection<RewardGroupSettings> RewardGroups { get; set; } =
    [
        new() { GroupNumber = 1, X = 1050, Y = 280 }
    ];
    public ObservableCollection<RateIndicatorSettings> RateIndicators { get; set; } =
    [
        new() { Kind = RateIndicatorKind.XpPerHour, X = 1050, Y = 410, TextColor = "#FF6FD8FF" },
        new() { Kind = RateIndicatorKind.TimeToLevel, X = 1050, Y = 442, TextColor = "#FFFFFFFF" },
        new() { Kind = RateIndicatorKind.TimeToJobLevel, X = 1050, Y = 474, TextColor = "#FFC9A7FF" },
        new() { Kind = RateIndicatorKind.ZenyPerHour, X = 1050, Y = 506, TextColor = "#FFFFE16A" },
        new() { Kind = RateIndicatorKind.ZenyTotal, X = 1050, Y = 538, TextColor = "#FFFFD45C" },
        new() { Kind = RateIndicatorKind.SessionTime, X = 1050, Y = 570, TextColor = "#FFFFFFFF" }
    ];
    public ObservableCollection<MarketWatchRule> MarketWatchRules { get; set; } = [];
    public MarketWatchNotificationSettings MarketWatchNotifications { get; set; } = new();
    [JsonIgnore] public ObservableCollection<MarketWatchNotification> ActiveMarketNotifications { get; } = [];
    public ObservableCollection<GoldGoblinPriceRule> GoldGoblinPriceRules { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<string> HotkeyChoices { get; } =
    [
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Insert", "Delete", "Home", "End", "PageUp", "PageDown", "Pause", "Escape"
    ];

    [JsonIgnore]
    public ObservableCollection<EffectNotification> ActiveNotifications { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifySettingsApplied()
    {
        // Some persisted fields are auto-properties; refresh their bindings too.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public void SyncCooldownGroups()
    {
        var numbers = Skills.Select(skill => Math.Max(1, skill.GroupNumber)).Distinct().OrderBy(number => number).ToList();
        if (numbers.Count == 0) numbers.Add(1);
        foreach (var number in numbers)
        {
            if (CooldownGroups.Any(group => group.GroupNumber == number)) continue;
            CooldownGroups.Add(new CooldownGroupSettings
            {
                GroupNumber = number,
                Name = number == 1 ? "Основная панель" : $"Группа {number}"
            });
        }
        foreach (var group in CooldownGroups)
        {
            group.Skills.Clear();
            foreach (var skill in Skills.Where(skill => Math.Max(1, skill.GroupNumber) == group.GroupNumber))
                group.Skills.Add(skill);
        }
    }

    public void SyncBuffPanelGroups()
    {
        var groupNumbers = Effects
            .Where(effect => effect.Enabled && effect.ShowOnBuffPanel && effect.GroupNumber > 0)
            .Select(effect => effect.GroupNumber).Distinct().OrderBy(number => number).ToList();

        foreach (var number in groupNumbers)
        {
            var group = BuffPanelGroups.FirstOrDefault(item => item.GroupNumber == number);
            if (group is null)
            {
                group = new BuffPanelGroup
                {
                    GroupNumber = number,
                    X = 900,
                    Y = 360 + BuffPanelGroups.Count * 58
                };
                BuffPanelGroups.Add(group);
            }

            group.Effects.Clear();
            foreach (var effect in Effects.Where(effect => effect.Enabled &&
                         effect.ShowOnBuffPanel && effect.GroupNumber == number))
                group.Effects.Add(effect);
        }

        foreach (var group in BuffPanelGroups.Where(group => !groupNumbers.Contains(group.GroupNumber)))
            group.Effects.Clear();
    }

    public void SyncRewardGroups()
    {
        AddMissingRewardChannel(RewardKind.Loot, 1, "#FFFFD45C");
        AddMissingRewardChannel(RewardKind.Zeny, 2, "#FFFFE16A");
        AddMissingRewardChannel(RewardKind.Experience, 3, "#FF6FD8FF");

        var groupNumbers = RewardChannels.Where(channel => channel.Enabled)
            .Select(channel => channel.GroupNumber).Distinct().OrderBy(number => number).ToList();
        foreach (var number in groupNumbers)
        {
            if (RewardGroups.Any(group => group.GroupNumber == number)) continue;
            RewardGroups.Add(new RewardGroupSettings
            {
                GroupNumber = number,
                X = 1050,
                Y = 280 + RewardGroups.Count * 170
            });
        }
    }

    public void SyncRateIndicators()
    {
        AddMissingRateIndicator(RateIndicatorKind.XpPerHour, 410, "#FF6FD8FF");
        AddMissingRateIndicator(RateIndicatorKind.TimeToLevel, 442, "#FFFFFFFF");
        AddMissingRateIndicator(RateIndicatorKind.TimeToJobLevel, 474, "#FFC9A7FF");
        AddMissingRateIndicator(RateIndicatorKind.ZenyPerHour, 506, "#FFFFE16A");
        AddMissingRateIndicator(RateIndicatorKind.ZenyTotal, 538, "#FFFFD45C");
        AddMissingRateIndicator(RateIndicatorKind.SessionTime, 570, "#FFFFFFFF");
    }

    public RewardChannelSettings? RewardChannel(RewardKind kind) =>
        RewardChannels.FirstOrDefault(channel => channel.Kind == kind && channel.Enabled);

    private void AddMissingRewardChannel(RewardKind kind, int order, string color)
    {
        if (RewardChannels.Any(channel => channel.Kind == kind)) return;
        RewardChannels.Add(new RewardChannelSettings
        {
            Kind = kind,
            GroupNumber = 1,
            Order = order,
            TextColor = color
        });
    }

    private void AddMissingRateIndicator(RateIndicatorKind kind, double y, string color)
    {
        if (RateIndicators.Any(indicator => indicator.Kind == kind)) return;
        RateIndicators.Add(new RateIndicatorSettings
        {
            Kind = kind,
            X = 1050,
            Y = y,
            TextColor = color
        });
    }

    private static string NormalizeHotkey(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName?.EndsWith("Hotkey", StringComparison.Ordinal) == true)
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(QuestHotkeyConflict)));
    }
}
