using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class BuffPanelGroup : INotifyPropertyChanged
{
    private int _groupNumber = 1;
    private double _x = 900;
    private double _y = 360;
    private double _iconSize = 40;
    private double _iconPadding;
    private double _gap = 4;
    private double _activeOpacityPercent = 100;
    private double _inactiveOpacityPercent = 42;
    private bool _isPlacementPreview;

    public int GroupNumber { get => _groupNumber; set => Set(ref _groupNumber, Math.Clamp(value, 1, 999)); }
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double IconSize { get => _iconSize; set => Set(ref _iconSize, Math.Clamp(value, 20, 180)); }
    public double IconPadding { get => _iconPadding; set => Set(ref _iconPadding, Math.Clamp(value, 0, 40)); }
    public double Gap { get => _gap; set => Set(ref _gap, Math.Clamp(value, 0, 40)); }
    public double ActiveOpacityPercent { get => _activeOpacityPercent; set => Set(ref _activeOpacityPercent, Math.Clamp(value, 0, 100)); }
    public double InactiveOpacityPercent { get => _inactiveOpacityPercent; set => Set(ref _inactiveOpacityPercent, Math.Clamp(value, 0, 100)); }

    [JsonIgnore]
    public bool IsPlacementPreview { get => _isPlacementPreview; set => Set(ref _isPlacementPreview, value); }

    [JsonIgnore]
    public ObservableCollection<TrackedEffect> Effects { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
