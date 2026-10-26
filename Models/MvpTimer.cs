using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class MvpTimer : INotifyPropertyChanged
{
    private string _name = "MVP";
    private int _monsterId;
    private double _durationMinutes = 60;
    private DateTime? _endsAtUtc;
    private double _remainingSeconds;
    private bool _enabled = true;
    private bool _completionSoundEnabled;
    private string _completionSoundPath = "";
    private double _completionSoundVolumePercent = 100;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value); }
    public int MonsterId { get => _monsterId; set => Set(ref _monsterId, Math.Max(0, value)); }
    public double DurationMinutes { get => _durationMinutes; set => Set(ref _durationMinutes, Math.Clamp(value, 0.1, 10080)); }
    public DateTime? EndsAtUtc
    {
        get => _endsAtUtc;
        set
        {
            if (!Set(ref _endsAtUtc, value)) return;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(StateText));
        }
    }
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) OnPropertyChanged(nameof(IsVisible)); } }
    public bool CompletionSoundEnabled { get => _completionSoundEnabled; set => Set(ref _completionSoundEnabled, value); }
    public string CompletionSoundPath { get => _completionSoundPath; set => Set(ref _completionSoundPath, value ?? ""); }
    public double CompletionSoundVolumePercent { get => _completionSoundVolumePercent; set => Set(ref _completionSoundVolumePercent, Math.Clamp(value, 0, 100)); }

    [JsonIgnore]
    public double RemainingSeconds
    {
        get => _remainingSeconds;
        set
        {
            if (!Set(ref _remainingSeconds, Math.Max(0, value))) return;
            OnPropertyChanged(nameof(RemainingText));
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(StateText));
        }
    }

    [JsonIgnore] public bool IsRunning => EndsAtUtc.HasValue && RemainingSeconds > 0;
    [JsonIgnore] public bool IsVisible => Enabled && IsRunning;
    [JsonIgnore] public string StateText => Services.LocalizationService.Get(
        IsRunning ? "TimerRunning" : EndsAtUtc.HasValue ? "TimerCompleted" : "TimerNotStarted");
    [JsonIgnore] public bool CompletionSoundPlayed { get; set; }
    [JsonIgnore]
    public string RemainingText
    {
        get
        {
            var time = TimeSpan.FromSeconds(Math.Ceiling(RemainingSeconds));
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes:00}:{time.Seconds:00}";
        }
    }
    [JsonIgnore] public string DisplayText => $"{Name} — {RemainingText}";

    public void Restart()
    {
        CompletionSoundPlayed = false;
        EndsAtUtc = DateTime.UtcNow.AddMinutes(DurationMinutes);
        RemainingSeconds = DurationMinutes * 60;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(StateText));
    }

    public void Reset()
    {
        CompletionSoundPlayed = true;
        EndsAtUtc = null;
        RemainingSeconds = 0;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(StateText));
    }

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
