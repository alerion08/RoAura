using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public enum RateIndicatorKind
{
    XpPerHour,
    TimeToLevel,
    ZenyPerHour,
    TimeToJobLevel,
    ZenyTotal,
    SessionTime
}

public sealed class RateIndicatorSettings : INotifyPropertyChanged
{
    private bool _enabled = true;
    private double _x = 1050;
    private double _y = 400;
    private double _textSize = 22;
    private string _textColor = "#FFFFFFFF";
    private double _opacityPercent = 100;
    private int _groupNumber = 1;
    private string _valueText = "";
    private bool _isPlacementPreview;

    public RateIndicatorKind Kind { get; set; }
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!Set(ref _enabled, value)) return;
            OnPropertyChanged(nameof(IsVisible));
        }
    }
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double TextSize { get => _textSize; set => Set(ref _textSize, Math.Clamp(value, 10, 72)); }
    public string TextColor { get => _textColor; set => Set(ref _textColor, value ?? "#FFFFFFFF"); }
    public int GroupNumber { get => _groupNumber; set => Set(ref _groupNumber, Math.Clamp(value, 0, 99)); }
    public double OpacityPercent
    {
        get => _opacityPercent;
        set
        {
            if (!Set(ref _opacityPercent, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(Opacity));
        }
    }

    [JsonIgnore] public string DisplayName => Kind switch
    {
        RateIndicatorKind.XpPerHour => "XP per hour",
        RateIndicatorKind.TimeToLevel => "Time to level",
        RateIndicatorKind.TimeToJobLevel => "Time to Job level",
        RateIndicatorKind.ZenyPerHour => "Zeny per hour",
        RateIndicatorKind.ZenyTotal => "Zeny Total",
        _ => "Session time"
    };
    [JsonIgnore] public string ValueText { get => _valueText; set => Set(ref _valueText, value); }
    [JsonIgnore] public double Opacity => OpacityPercent / 100d;
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
    [JsonIgnore] public bool IsVisible => Enabled || IsPlacementPreview;

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
