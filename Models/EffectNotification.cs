using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RoAura.Models;

public sealed class EffectNotification : INotifyPropertyChanged
{
    private double _x;
    private double _y;

    public Guid Id { get; } = Guid.NewGuid();
    public Guid EffectId { get; init; }
    public int GroupNumber { get; init; }
    public string Text { get; init; } = "";
    public string IconBase64 { get; init; } = "";
    public string Color { get; init; } = "#FFFFFFFF";
    public string BackgroundColor { get; init; } = "#D911141B";
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double IconSize { get; init; } = 72;
    public double IconPadding { get; init; }
    public double TextSize { get; init; } = 30;
    public double Opacity { get; init; } = 1;
    public bool ShowIcon { get; init; } = true;
    public bool ShowBackground { get; init; } = true;
    public bool ShowBorder { get; init; } = true;
    public double BaseX { get; init; }
    public double BaseY { get; init; }
    public bool IsPlacementPreview { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
