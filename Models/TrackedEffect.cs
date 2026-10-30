using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using RoAura.Services;

namespace RoAura.Models;

public sealed class TrackedEffect : INotifyPropertyChanged
{
    public CharacterScope CharacterScope { get; set; } = new();

    private string _name = "Новый эффект";
    private bool _enabled = true;
    private int _statusId;
    private string _templateImageBase64 = "";
    private string _templateFileName = "";
    private bool _notifyOnAppeared = true;
    private bool _notifyOnDisappeared = true;
    private string _appearedText = "ЭФФЕКТ ПОЯВИЛСЯ";
    private string _disappearedText = "ЭФФЕКТ ЗАКОНЧИЛСЯ";
    private double _notificationDurationSeconds = 2.5;
    private double _notificationSize = 72;
    private string _notificationColor = "#FFFFFFFF";
    private double _notificationX = 900;
    private double _notificationY = 250;
    private bool _soundEnabled;
    private string _soundPath = "";
    private bool _appearedSoundEnabled;
    private string _appearedSoundPath = "";
    private bool _disappearedSoundEnabled;
    private string _disappearedSoundPath = "";
    private bool _isActive;
    private int _groupNumber = 1;
    private double _notificationIconSize = 72;
    private double _notificationIconPadding;
    private double _notificationTextSize = 30;
    private double _notificationOpacityPercent = 100;
    private bool _showNotificationIcon = true;
    private string _notificationBackgroundColor = "#D911141B";
    private bool _showNotificationBackground = true;
    private bool _showNotificationBorder = true;
    private double _appearedSoundVolumePercent = 100;
    private double _disappearedSoundVolumePercent = 100;
    private bool _showOnBuffPanel;
    private double _repeatProtectionSeconds = 2;
    private string _currentStateText = "ожидание пакета";
    private bool _showDurationText = true;
    private bool _showDurationFill = true;
    private double _remainingSeconds;
    private double _totalDurationSeconds;
    private DateTime? _durationEndsAtUtc;

    private string _learningObservation = "";
    [JsonIgnore]
    public string LearningObservation { get => _learningObservation; set => Set(ref _learningObservation, value); }

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public int StatusId { get => _statusId; set => Set(ref _statusId, Math.Clamp(value, 0, ushort.MaxValue)); }
    public string TemplateImageBase64
    {
        get => _templateImageBase64;
        set
        {
            if (!Set(ref _templateImageBase64, value)) return;
            OnPropertyChanged(nameof(HasTemplate));
            OnPropertyChanged(nameof(TemplateStatusText));
        }
    }
    public string TemplateFileName { get => _templateFileName; set { if (Set(ref _templateFileName, value)) OnPropertyChanged(nameof(TemplateStatusText)); } }
    public bool NotifyOnAppeared { get => _notifyOnAppeared; set => Set(ref _notifyOnAppeared, value); }
    public bool NotifyOnDisappeared { get => _notifyOnDisappeared; set => Set(ref _notifyOnDisappeared, value); }
    public string AppearedText { get => _appearedText; set => Set(ref _appearedText, value); }
    public string DisappearedText { get => _disappearedText; set => Set(ref _disappearedText, value); }
    public double NotificationDurationSeconds { get => _notificationDurationSeconds; set => Set(ref _notificationDurationSeconds, Math.Clamp(value, 0.5, 30)); }
    public double NotificationSize { get => _notificationSize; set => Set(ref _notificationSize, Math.Clamp(value, 40, 180)); }
    public string NotificationColor { get => _notificationColor; set => Set(ref _notificationColor, value); }
    public double NotificationX { get => _notificationX; set => Set(ref _notificationX, value); }
    public double NotificationY { get => _notificationY; set => Set(ref _notificationY, value); }
    public bool SoundEnabled { get => _soundEnabled; set => Set(ref _soundEnabled, value); }
    public string SoundPath { get => _soundPath; set => Set(ref _soundPath, value); }
    public bool AppearedSoundEnabled { get => _appearedSoundEnabled; set => Set(ref _appearedSoundEnabled, value); }
    public string AppearedSoundPath { get => _appearedSoundPath; set => Set(ref _appearedSoundPath, value); }
    public bool DisappearedSoundEnabled { get => _disappearedSoundEnabled; set => Set(ref _disappearedSoundEnabled, value); }
    public string DisappearedSoundPath { get => _disappearedSoundPath; set => Set(ref _disappearedSoundPath, value); }
    public int GroupNumber { get => _groupNumber; set => Set(ref _groupNumber, Math.Clamp(value, 0, 999)); }
    public double NotificationIconSize { get => _notificationIconSize; set => Set(ref _notificationIconSize, Math.Clamp(value, 20, 180)); }
    public double NotificationIconPadding { get => _notificationIconPadding; set => Set(ref _notificationIconPadding, Math.Clamp(value, 0, 40)); }
    public double NotificationTextSize { get => _notificationTextSize; set => Set(ref _notificationTextSize, Math.Clamp(value, 10, 96)); }
    public double NotificationOpacityPercent { get => _notificationOpacityPercent; set => Set(ref _notificationOpacityPercent, Math.Clamp(value, 0, 100)); }
    public bool ShowNotificationIcon { get => _showNotificationIcon; set => Set(ref _showNotificationIcon, value); }
    public string NotificationBackgroundColor { get => _notificationBackgroundColor; set => Set(ref _notificationBackgroundColor, value); }
    public bool ShowNotificationBackground { get => _showNotificationBackground; set => Set(ref _showNotificationBackground, value); }
    public bool ShowNotificationBorder { get => _showNotificationBorder; set => Set(ref _showNotificationBorder, value); }
    public double AppearedSoundVolumePercent { get => _appearedSoundVolumePercent; set => Set(ref _appearedSoundVolumePercent, Math.Clamp(value, 0, 100)); }
    public double DisappearedSoundVolumePercent { get => _disappearedSoundVolumePercent; set => Set(ref _disappearedSoundVolumePercent, Math.Clamp(value, 0, 100)); }
    public bool ShowOnBuffPanel { get => _showOnBuffPanel; set => Set(ref _showOnBuffPanel, value); }
    public double RepeatProtectionSeconds { get => _repeatProtectionSeconds; set => Set(ref _repeatProtectionSeconds, Math.Clamp(value, 0, 60)); }
    public bool ShowDurationText { get => _showDurationText; set => Set(ref _showDurationText, value); }
    public bool ShowDurationFill { get => _showDurationFill; set => Set(ref _showDurationFill, value); }

    [JsonIgnore]
    public bool HasTemplate => !string.IsNullOrWhiteSpace(TemplateImageBase64);

    [JsonIgnore]
    public string TemplateStatusText => HasTemplate
        ? string.IsNullOrWhiteSpace(TemplateFileName) ? LocalizationService.Get("Loaded") : TemplateFileName
        : LocalizationService.Get("NotSelected");

    [JsonIgnore]
    public string CurrentStateText { get => _currentStateText; set => Set(ref _currentStateText, value); }

    [JsonIgnore]
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    [JsonIgnore]
    public DateTime? DurationEndsAtUtc { get => _durationEndsAtUtc; set => Set(ref _durationEndsAtUtc, value); }

    [JsonIgnore]
    public double RemainingSeconds
    {
        get => _remainingSeconds;
        set
        {
            if (!Set(ref _remainingSeconds, Math.Max(0, value))) return;
            OnPropertyChanged(nameof(RemainingText));
            OnPropertyChanged(nameof(DurationProgress));
        }
    }

    [JsonIgnore]
    public double TotalDurationSeconds
    {
        get => _totalDurationSeconds;
        set { if (Set(ref _totalDurationSeconds, Math.Max(0, value))) OnPropertyChanged(nameof(DurationProgress)); }
    }

    [JsonIgnore]
    public string RemainingText
    {
        get
        {
            var rounded = (int)Math.Ceiling(RemainingSeconds);
            return RemainingSeconds >= 60
                ? $"{rounded / 60:00}:{rounded % 60:00}"
                : RemainingSeconds >= 10 ? rounded.ToString("0") : RemainingSeconds.ToString("0.0");
        }
    }
    [JsonIgnore] public double DurationProgress => TotalDurationSeconds <= 0 ? 0 : Math.Clamp(RemainingSeconds / TotalDurationSeconds * 100, 0, 100);

    public void ApplyDuration(uint remainingMs, uint totalMs)
    {
        if (remainingMs is 0 or uint.MaxValue)
        {
            DurationEndsAtUtc = null;
            RemainingSeconds = 0;
            TotalDurationSeconds = 0;
            return;
        }
        RemainingSeconds = remainingMs / 1000d;
        TotalDurationSeconds = (totalMs is > 0 and < uint.MaxValue ? totalMs : remainingMs) / 1000d;
        DurationEndsAtUtc = DateTime.UtcNow.AddMilliseconds(remainingMs);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
