using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class CooldownGroupSettings : INotifyPropertyChanged
{
    private int _groupNumber = 1;
    private string _name = "";
    private string _direction = "Horizontal";
    private double _gap = 6;

    public int GroupNumber { get => _groupNumber; set => Set(ref _groupNumber, Math.Clamp(value, 1, 999)); }
    public string Name { get => _name; set => Set(ref _name, value?.Trim() ?? ""); }
    public string Direction { get => _direction; set => Set(ref _direction, value == "Vertical" ? "Vertical" : "Horizontal"); }
    public double Gap { get => _gap; set => Set(ref _gap, Math.Clamp(value, 0, 100)); }

    [JsonIgnore]
    public ObservableCollection<SkillAura> Skills { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? $"Group {GroupNumber}" : Name;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
