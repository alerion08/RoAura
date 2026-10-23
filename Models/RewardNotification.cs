using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RoAura.Models;

public sealed class RewardNotification : INotifyPropertyChanged
{
    private string _text = "";
    private string _color = "#FFFFFFFF";
    private long _amount;
    private double _percentage;
    private bool _hasPercentage;

    public Guid Id { get; } = Guid.NewGuid();
    public RewardKind Kind { get; init; }
    public string AggregationKey { get; init; } = "";
    public string ItemName { get; init; } = "";
    public long Amount { get => _amount; set => Set(ref _amount, value); }
    public double Percentage { get => _percentage; set => Set(ref _percentage, value); }
    public bool HasPercentage { get => _hasPercentage; set => Set(ref _hasPercentage, value); }
    public string Text { get => _text; set => Set(ref _text, value); }
    public string Color { get => _color; set => Set(ref _color, value); }
    public DateTime LastUpdatedUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
