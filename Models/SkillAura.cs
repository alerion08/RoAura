using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using RoAura.Services;

namespace RoAura.Models;

public sealed class SkillAura : INotifyPropertyChanged
{
    private string _name = "Новый скилл";
    private double _cooldownSeconds = 5;
    private double _x = 100;
    private double _y = 100;
    private bool _enabled = true;
    private string _iconPath = "";
    private string _iconImageBase64 = "";
    private string _iconFileName = "";
    private int _packetSkillId;
    private double _remainingSeconds;
    private bool _isReady = true;
    private int _groupNumber = 1;
    private double _individualIconSize = 72;
    private double _iconPadding;
    private double _readyOpacityPercent = 100;
    private double _cooldownOpacityPercent = 68;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value); }
    public double CooldownSeconds { get => _cooldownSeconds; set => Set(ref _cooldownSeconds, Math.Max(0.1, value)); }
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public int PacketSkillId { get => _packetSkillId; set => Set(ref _packetSkillId, Math.Clamp(value, 0, ushort.MaxValue)); }
    public int GroupNumber { get => _groupNumber; set => Set(ref _groupNumber, Math.Clamp(value, 0, 999)); }
    public double IndividualIconSize { get => _individualIconSize; set => SetWithOpacity(ref _individualIconSize, Math.Clamp(value, 20, 180)); }
    public double IconPadding { get => _iconPadding; set => Set(ref _iconPadding, Math.Clamp(value, 0, 40)); }
    public double ReadyOpacityPercent { get => _readyOpacityPercent; set => SetWithOpacity(ref _readyOpacityPercent, Math.Clamp(value, 0, 100)); }
    public double CooldownOpacityPercent { get => _cooldownOpacityPercent; set => SetWithOpacity(ref _cooldownOpacityPercent, Math.Clamp(value, 0, 100)); }
    public string IconImageBase64 { get => _iconImageBase64; set { if (Set(ref _iconImageBase64, value)) OnPropertyChanged(nameof(IconStatusText)); } }
    public string IconFileName { get => _iconFileName; set { if (Set(ref _iconFileName, value)) OnPropertyChanged(nameof(IconStatusText)); } }

    // Pre-1.0 settings stored only an external path. SettingsStore imports it once.
    public string IconPath { get => _iconPath; set => Set(ref _iconPath, value); }

    [JsonIgnore]
    public string IconStatusText => string.IsNullOrWhiteSpace(IconImageBase64)
        ? LocalizationService.Get("NotSelected")
        : string.IsNullOrWhiteSpace(IconFileName) ? LocalizationService.Get("Loaded") : IconFileName;

    [JsonIgnore]
    public double RemainingSeconds
    {
        get => _remainingSeconds;
        set
        {
            if (Set(ref _remainingSeconds, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(CooldownNumberText));
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(ElapsedProgress));
            }
        }
    }

    [JsonIgnore]
    public bool IsReady
    {
        get => _isReady;
        set
        {
            if (Set(ref _isReady, value))
            {
                OnPropertyChanged(nameof(ElapsedProgress));
                OnPropertyChanged(nameof(CurrentOpacity));
            }
        }
    }

    [JsonIgnore]
    public string CooldownNumberText => RemainingSeconds >= 10
        ? Math.Ceiling(RemainingSeconds).ToString("0")
        : RemainingSeconds.ToString("0.0");

    [JsonIgnore]
    public double Progress => CooldownSeconds <= 0 ? 0 : RemainingSeconds / CooldownSeconds * 100;

    [JsonIgnore]
    public double ElapsedProgress => IsReady ? 0 : Math.Clamp(100 - Progress, 0, 100);

    [JsonIgnore]
    public double CurrentOpacity => (IsReady ? ReadyOpacityPercent : CooldownOpacityPercent) / 100d;

    [JsonIgnore]
    public string ShortName => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2).Select(part => char.ToUpperInvariant(part[0])));

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        if (name == nameof(Name)) OnPropertyChanged(nameof(ShortName));
        return true;
    }

    private double SetWithOpacity(ref double field, double value, [CallerMemberName] string? name = null)
    {
        if (Set(ref field, value, name)) OnPropertyChanged(nameof(CurrentOpacity));
        return field;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
