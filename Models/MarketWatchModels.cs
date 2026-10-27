using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class MarketWatchRule
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public bool WithoutPrice { get; set; }
    public long MaximumPrice { get; set; }
    [JsonIgnore] public string Caption => $"{ItemName} · #{ItemId}";
    [JsonIgnore] public string PriceCaption => WithoutPrice ? "—" : $"≤ {MaximumPrice:N0} Zeny";
}

public sealed class MarketWatchNotificationSettings : INotifyPropertyChanged
{
    private bool _enabled = true, _soundEnabled;
    private string _text = "%name появился по цене %price Zeny", _icon = "", _sound = "";
    private string _background = "#DD102B50", _textColor = "#FFFFFFFF";
    private double _iconSize = 40, _textSize = 22, _padding = 10, _gap = 6, _duration = 6, _x = 400, _y = 120;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string TextTemplate { get => _text; set => Set(ref _text, value ?? ""); }
    public string IconImageBase64 { get => _icon; set => Set(ref _icon, value ?? ""); }
    public double IconSize { get => _iconSize; set => Set(ref _iconSize, Limit(value, 0, 180, 40)); }
    public double TextSize { get => _textSize; set => Set(ref _textSize, Limit(value, 10, 72, 22)); }
    public string Background { get => _background; set => Set(ref _background, value ?? "#DD102B50"); }
    public string TextColor { get => _textColor; set => Set(ref _textColor, value ?? "#FFFFFFFF"); }
    public double Padding { get => _padding; set => Set(ref _padding, Limit(value, 0, 80, 10)); }
    public double Gap { get => _gap; set => Set(ref _gap, Limit(value, 0, 80, 6)); }
    public double DurationSeconds { get => _duration; set => Set(ref _duration, Limit(value, .5, 120, 6)); }
    public double X { get => _x; set => Set(ref _x, double.IsFinite(value) ? value : 400); }
    public double Y { get => _y; set => Set(ref _y, double.IsFinite(value) ? value : 120); }
    public bool SoundEnabled { get => _soundEnabled; set => Set(ref _soundEnabled, value); }
    public string SoundPath { get => _sound; set => Set(ref _sound, value ?? ""); }
    private static double Limit(double v, double min, double max, double fallback) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed record MarketWatchMatch(int ItemId, string ItemName, long Price);
public sealed record MarketWatchNotification(string Text, System.Windows.Media.ImageSource? Icon, DateTime ExpiresAtUtc);
