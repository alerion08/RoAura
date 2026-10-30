using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class MvpTimer : INotifyPropertyChanged
{
    private string _name = "MVP";
    private string _monsterImageUrl = "";
    private int _monsterId;
    private double _durationMinutes = 60;
    private DateTime? _killedAtUtc;
    private string _killTimeInput = "";
    private DateTime? _endsAtUtc;
    private double _remainingSeconds;
    private bool _enabled = true;
    private bool _completionSoundEnabled;
    private string _completionSoundPath = "";
    private double _completionSoundVolumePercent = 100;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value); }
    public int MonsterId
    {
        get => _monsterId;
        set
        {
            if (!Set(ref _monsterId, Math.Max(0, value))) return;
            _monsterImageUrl = "";
            OnPropertyChanged(nameof(MonsterImagePath));
        }
    }
    public double DurationMinutes { get => _durationMinutes; set => Set(ref _durationMinutes, Math.Clamp(value, 0.1, 10080)); }
    public DateTime? KilledAtUtc
    {
        get => _killedAtUtc;
        set
        {
            if (!Set(ref _killedAtUtc, value)) return;
            _killTimeInput = value?.ToLocalTime().ToString("HH:mm:ss") ?? "";
            OnPropertyChanged(nameof(KilledAtText));
            OnPropertyChanged(nameof(KillTimeInput));
            OnPropertyChanged(nameof(HasKillTime));
        }
    }
    [JsonIgnore]
    public string KillTimeInput
    {
        get => _killTimeInput;
        set => Set(ref _killTimeInput, value ?? "");
    }
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
    [JsonIgnore] public bool HasKillTime => KilledAtUtc.HasValue;
    [JsonIgnore] public string KilledAtText => KilledAtUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "";
    [JsonIgnore] public IReadOnlyList<MonsterSpawnEntry> RespawnHints { get; private set; } = [];
    [JsonIgnore]
    public string MonsterImagePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_monsterImageUrl)) return _monsterImageUrl;
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "MvpImages", $"{MonsterId}.png");
            return File.Exists(path) ? path : "";
        }
    }
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

    public void SetMonsterDetails(MonsterEntry? monster)
    {
        _monsterImageUrl = monster?.ImageUrl ?? "";
        RespawnHints = monster is null ? Array.Empty<MonsterSpawnEntry>() : monster.Spawns;
        OnPropertyChanged(nameof(RespawnHints));
        OnPropertyChanged(nameof(MonsterImagePath));
    }

    public void Restart()
    {
        StartFromKillTime(DateTime.Now);
    }

    public void StartFromKillTime(DateTime killedAtLocal)
    {
        CompletionSoundPlayed = false;
        var local = killedAtLocal.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(killedAtLocal, DateTimeKind.Local)
            : killedAtLocal.ToLocalTime();
        KilledAtUtc = local.ToUniversalTime();
        EndsAtUtc = KilledAtUtc.Value.AddMinutes(DurationMinutes);
        RemainingSeconds = DurationMinutes * 60;
        if (EndsAtUtc <= DateTime.UtcNow)
            RemainingSeconds = 0;
        else
            RemainingSeconds = (EndsAtUtc.Value - DateTime.UtcNow).TotalSeconds;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(StateText));
    }

    public void StartFromKillClock(TimeSpan killTime, DateTime? nowLocal = null)
    {
        var now = (nowLocal ?? DateTime.Now).ToLocalTime();
        var killedAt = now.Date.Add(killTime);
        if (killedAt > now) killedAt = killedAt.AddDays(-1);
        StartFromKillTime(killedAt);
    }

    public void Reset()
    {
        CompletionSoundPlayed = true;
        KilledAtUtc = null;
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
