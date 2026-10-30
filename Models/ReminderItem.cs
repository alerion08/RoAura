using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using RoAura.Services;

namespace RoAura.Models;

public sealed class ReminderItem : INotifyPropertyChanged
{
    public CharacterScope CharacterScope { get; set; } = new();

    private string _name = "New reminder";
    private bool _enabled = true;
    private int _statusId;
    private string _iconImageBase64 = "";
    private string _iconFileName = "";
    private string _text = "BUFF MISSING";
    private double _x = 900;
    private double _y = 500;
    private int _groupNumber = 1;
    private double _iconSize = 64;
    private double _iconPadding;
    private double _textSize = 18;
    private double _opacityPercent = 100;
    private bool _showIcon = true;
    private string _textColor = "#FFFFFFFF";
    private string _backgroundColor = "#D911141B";
    private bool _showBackground = true;
    private bool _showBorder = true;
    private string _borderColor = "#B8C5CBD4";
    private bool _isStatusPresent;
    private bool _isPlacementPreview;

    private string _learningObservation = "";
    [JsonIgnore]
    public string LearningObservation { get => _learningObservation; set => Set(ref _learningObservation, value); }

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value); }
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) OnPropertyChanged(nameof(IsVisible)); } }
    public int StatusId { get => _statusId; set => Set(ref _statusId, Math.Clamp(value, 0, ushort.MaxValue)); }
    public string IconImageBase64 { get => _iconImageBase64; set { if (Set(ref _iconImageBase64, value)) OnPropertyChanged(nameof(IconStatusText)); } }
    public string IconFileName { get => _iconFileName; set { if (Set(ref _iconFileName, value)) OnPropertyChanged(nameof(IconStatusText)); } }
    public string Text { get => _text; set => Set(ref _text, value); }
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public int GroupNumber { get => _groupNumber; set => Set(ref _groupNumber, Math.Clamp(value, 0, 999)); }
    public double IconSize { get => _iconSize; set => Set(ref _iconSize, Math.Clamp(value, 20, 180)); }
    public double IconPadding { get => _iconPadding; set => Set(ref _iconPadding, Math.Clamp(value, 0, 40)); }
    public double TextSize { get => _textSize; set => Set(ref _textSize, Math.Clamp(value, 10, 96)); }
    public double OpacityPercent { get => _opacityPercent; set { if (Set(ref _opacityPercent, Math.Clamp(value, 0, 100))) OnPropertyChanged(nameof(Opacity)); } }
    public bool ShowIcon { get => _showIcon; set => Set(ref _showIcon, value); }
    public string TextColor { get => _textColor; set => Set(ref _textColor, value); }
    public string BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }
    public bool ShowBackground { get => _showBackground; set => Set(ref _showBackground, value); }
    public bool ShowBorder { get => _showBorder; set => Set(ref _showBorder, value); }
    public string BorderColor { get => _borderColor; set => Set(ref _borderColor, value); }

    [JsonIgnore]
    public bool IsStatusPresent
    {
        get => _isStatusPresent;
        set
        {
            if (!Set(ref _isStatusPresent, value)) return;
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(CurrentStateText));
        }
    }

    [JsonIgnore]
    public bool IsPlacementPreview
    {
        get => _isPlacementPreview;
        set
        {
            if (!Set(ref _isPlacementPreview, value)) return;
            OnPropertyChanged(nameof(IsVisible));
        }
    }

    [JsonIgnore]
    public bool IsVisible => Enabled && (!IsStatusPresent || IsPlacementPreview);

    [JsonIgnore]
    public double Opacity => OpacityPercent / 100d;

    [JsonIgnore]
    public string IconStatusText => string.IsNullOrWhiteSpace(IconImageBase64)
        ? LocalizationService.Get("NotSelected")
        : string.IsNullOrWhiteSpace(IconFileName) ? LocalizationService.Get("Loaded") : IconFileName;

    [JsonIgnore]
    public string CurrentStateText => LocalizationService.Get(IsStatusPresent ? "StatusPresent" : "StatusMissing");

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
