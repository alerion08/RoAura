using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class LootList
{
    public string Server { get; set; } = "";
    public string Name { get; set; } = "";
    public bool ShowInTracker { get; set; }
    public ObservableCollection<LootListItem> Items { get; set; } = [];
}

public sealed class LootListItem : INotifyPropertyChanged
{
    private long _target = 1;
    private long _acquired;
    private bool _active = true;

    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public long Target { get => _target; set => Set(ref _target, Math.Clamp(value, 1, int.MaxValue)); }
    public long Acquired { get => _acquired; set => Set(ref _acquired, Math.Clamp(value, 0, int.MaxValue)); }
    public bool Active { get => _active; set => Set(ref _active, value); }
    [JsonIgnore] public long Remaining => Math.Max(0, Target - Acquired);
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
        if (name is nameof(Target) or nameof(Acquired)) PropertyChanged?.Invoke(this, new(nameof(Remaining)));
    }
}

public sealed class LootListTrackerSettings
{
    public bool Enabled { get; set; } = true;
    public double X { get; set; } = 12;
    public double Y { get; set; } = 420;
    public double Width { get; set; } = 300;
    public double TextSize { get; set; } = 14;
    public double Gap { get; set; } = 6;
    public double BackgroundOpacityPercent { get; set; } = 75;
    public string BackgroundColor { get; set; } = "#FF121C30";
    public string TextColor { get; set; } = "#FFFFFFFF";
    public bool Horizontal { get; set; }
}
