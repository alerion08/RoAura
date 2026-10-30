using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RoAura.Models;

public sealed class QuestOverlaySettings : INotifyPropertyChanged
{
    private bool _enabled = true, _buttons = true, _coordinates = true;
    private double _x = 12, _y = 200, _width = 320, _fontSize = 14, _opacity = 90, _gap = 6, _scale = 1;
    private string _textColor = "#FFFFFFFF";
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool ShowButtons { get => _buttons; set => Set(ref _buttons, value); }
    public bool ShowCoordinates { get => _coordinates; set => Set(ref _coordinates, value); }
    public double X { get => _x; set => Number(ref _x, value, -10000, 10000); }
    public double Y { get => _y; set => Number(ref _y, value, -10000, 10000); }
    public double Width { get => _width; set => Number(ref _width, value, 220, 640); }
    public double FontSize { get => _fontSize; set => Number(ref _fontSize, value, 10, 28); }
    public double OpacityPercent { get => _opacity; set => Number(ref _opacity, value, 10, 100); }
    public double CardGap { get => _gap; set => Number(ref _gap, value, 0, 24); }
    public double Scale { get => _scale; set => Number(ref _scale, value, .65, 1.5); }
    public string TextColor { get => _textColor; set => Set(ref _textColor, value ?? "#FFFFFFFF"); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Number(ref double field, double value, double min, double max, [CallerMemberName] string? name = null)
    { if (double.IsFinite(value)) Set(ref field, Math.Clamp(value, min, max), name); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}
