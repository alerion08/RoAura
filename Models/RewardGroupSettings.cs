using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class RewardGroupSettings : INotifyPropertyChanged
{
    private int _groupNumber = 1;
    private double _x = 1050;
    private double _y = 280;
    private double _textSize = 22;
    private double _durationSeconds = 3;
    private int _aggregationWindowMs = 650;
    private int _maxLines = 8;
    private double _gap = 4;
    private bool _isPlacementPreview;

    public RewardGroupSettings() => Notifications.CollectionChanged += Notifications_CollectionChanged;

    public int GroupNumber
    {
        get => _groupNumber;
        set
        {
            if (!Set(ref _groupNumber, Math.Clamp(value, 1, 99))) return;
            OnPropertyChanged(nameof(PreviewText));
        }
    }
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double TextSize { get => _textSize; set => Set(ref _textSize, Math.Clamp(value, 10, 72)); }
    public double DurationSeconds { get => _durationSeconds; set => Set(ref _durationSeconds, Math.Clamp(value, 0.5, 30)); }
    public int AggregationWindowMs { get => _aggregationWindowMs; set => Set(ref _aggregationWindowMs, Math.Clamp(value, 50, 5000)); }
    public int MaxLines { get => _maxLines; set => Set(ref _maxLines, Math.Clamp(value, 1, 30)); }
    public double Gap { get => _gap; set => Set(ref _gap, Math.Clamp(value, 0, 30)); }

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

    [JsonIgnore] public ObservableCollection<RewardNotification> Notifications { get; } = [];
    [JsonIgnore] public bool IsVisible => IsPlacementPreview || Notifications.Count > 0;
    [JsonIgnore] public string PreviewText => $"LOOT / ZENY / EXP · GROUP {GroupNumber}";

    private void Notifications_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(IsVisible));

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
