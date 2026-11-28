using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class MvpTimer : INotifyPropertyChanged
{
    private string _name = "MVP";
    private string _monsterImagePath = "";
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
    private int _displayNumber;
    private bool _isMvp = true;

    private string _locationCode = "";
    private double? _markerX, _markerY;
    public string LocationCode
    {
        get => _locationCode;
        set
        {
            var next = value?.Trim() ?? "";
            if (!Set(ref _locationCode, next)) return;
            ClearMarker();
            OnPropertyChanged(nameof(LocationLabel));
        }
    }
    public double? MarkerX { get => _markerX; set => Set(ref _markerX, NormalizeMarker(value)); }
    public double? MarkerY { get => _markerY; set => Set(ref _markerY, NormalizeMarker(value)); }
    private static double? NormalizeMarker(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? Math.Clamp(value.Value, 0, 1) : null;
    public void ClearMarker() { MarkerX = null; MarkerY = null; }

    private bool _quickRestart;
    public bool QuickRestart { get => _quickRestart; set => Set(ref _quickRestart, value); }

    private int _tabIndex;
    public int TabIndex { get => _tabIndex; set => Set(ref _tabIndex, Math.Clamp(value, 0, 2)); }

    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsMvp { get => _isMvp; set => Set(ref _isMvp, value); }
    public string Name { get => _name; set { if (Set(ref _name, value)) { OnPropertyChanged(nameof(NumberedName)); OnPropertyChanged(nameof(DisplayText)); } } }
    [JsonIgnore] public int DisplayNumber
    {
        get => _displayNumber;
        set { if (Set(ref _displayNumber, value)) OnPropertyChanged(nameof(NumberedName)); }
    }
    [JsonIgnore] public string NumberedName => DisplayNumber > 0 ? $"{DisplayNumber}. {Name}" : Name;
    public int MonsterId
    {
        get => _monsterId;
        set
        {
            if (!Set(ref _monsterId, Math.Max(0, value))) return;
            _monsterImagePath = "";
            OnPropertyChanged(nameof(MonsterImagePath));
            OnPropertyChanged(nameof(TimerImage));
        }
    }
    public double DurationMinutes { get => _durationMinutes; set => Set(ref _durationMinutes, Math.Clamp(value, 0.1, 10080)); }
    private double _respawnWindowStartMinutes;
    public double RespawnWindowStartMinutes
    {
        get => _respawnWindowStartMinutes;
        set => Set(ref _respawnWindowStartMinutes, double.IsFinite(value) ? Math.Clamp(value, 0, 10080) : 0);
    }

    private DateTime? _respawnWindowOpensAtUtc;
    public DateTime? RespawnWindowOpensAtUtc
    {
        get => _respawnWindowOpensAtUtc;
        set
        {
            if (!Set(ref _respawnWindowOpensAtUtc, value)) return;
            RefreshRespawnWindow();
            OnPropertyChanged(nameof(StateText));
        }
    }

    private bool _hasOverlappingRespawn;
    [JsonIgnore]
    public bool HasOverlappingRespawn
    {
        get => _hasOverlappingRespawn;
        internal set => Set(ref _hasOverlappingRespawn, value);
    }

    private double _secondsDisplayThresholdMinutes;
    [JsonIgnore]
    public double SecondsDisplayThresholdMinutes
    {
        get => _secondsDisplayThresholdMinutes;
        internal set
        {
            if (!Set(ref _secondsDisplayThresholdMinutes, value)) return;
            OnPropertyChanged(nameof(RemainingText));
            OnPropertyChanged(nameof(OverlayCountdown));
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    [JsonIgnore] public bool HasRespawnWindow => RespawnWindowOpensAtUtc is { } opening &&
        EndsAtUtc is { } closing && opening < closing;
    [JsonIgnore] public bool IsRespawnWindowOpen => IsRunning && HasRespawnWindow &&
        RemainingSeconds <= (EndsAtUtc!.Value - RespawnWindowOpensAtUtc!.Value).TotalSeconds;
    [JsonIgnore] public bool IsCompleted => EndsAtUtc.HasValue && !IsRunning;
    [JsonIgnore] public string OverlayCountdown => RemainingText;

    private void RefreshRespawnWindow()
    {
        OnPropertyChanged(nameof(IsRespawnWindowOpen));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(OverlayCountdown));
    }

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
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(StateText));
            RefreshRespawnWindow();
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
            RefreshRespawnWindow();
        }
    }

    [JsonIgnore] public bool IsRunning => EndsAtUtc.HasValue && RemainingSeconds > 0;
    private bool _showAfterCompletion;
    public bool ShowAfterCompletion
    {
        get => _showAfterCompletion;
        set { if (Set(ref _showAfterCompletion, value)) OnPropertyChanged(nameof(IsVisible)); }
    }

    [JsonIgnore] public bool IsVisible => Enabled && EndsAtUtc.HasValue && (IsRunning || ShowAfterCompletion);
    [JsonIgnore] public bool HasKillTime => KilledAtUtc.HasValue;
    [JsonIgnore] public string KilledAtText => KilledAtUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "";
    [JsonIgnore] public IReadOnlyList<MonsterSpawnEntry> RespawnHints { get; private set; } = [];
    [JsonIgnore] public byte[]? MonsterImagePng { get; private set; }
    [JsonIgnore] public object TimerImage => (object?)MonsterImagePng ?? MonsterImagePath;
    [JsonIgnore] public MonsterSpawnEntry? SelectedSpawn => RespawnHints.FirstOrDefault(spawn =>
        string.Equals(spawn.Code, LocationCode, StringComparison.OrdinalIgnoreCase));
    [JsonIgnore] public string LocationLabel => SelectedSpawn is { Name.Length: > 0 } spawn ? spawn.Name : LocationCode;
    [JsonIgnore]
    public string MonsterImagePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_monsterImagePath)) return _monsterImagePath;
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "MvpImages", $"{MonsterId}.png");
            return File.Exists(path) ? path : "";
        }
    }
    [JsonIgnore] public string StateText => Services.LocalizationService.Get(
        IsRespawnWindowOpen ? "RespawnWindowOpen" : IsRunning ? "TimerRunning" : EndsAtUtc.HasValue ? "TimerCompleted" : "TimerNotStarted");
    [JsonIgnore] public bool CompletionSoundPlayed { get; set; }
    [JsonIgnore]
    public string RemainingText
    {
        get
        {
            if (SecondsDisplayThresholdMinutes > 0 && RemainingSeconds > SecondsDisplayThresholdMinutes * 60)
            {
                var minutes = (long)Math.Ceiling(RemainingSeconds / 60);
                return $"{minutes / 60:00}:{minutes % 60:00}";
            }
            var time = TimeSpan.FromSeconds(Math.Ceiling(RemainingSeconds));
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes:00}:{time.Seconds:00}";
        }
    }
    [JsonIgnore] public string DisplayText => $"{Name} — {RemainingText}";

    public void SelectFirstLocationIfMissing()
    {
        if (string.IsNullOrWhiteSpace(LocationCode) && RespawnHints.FirstOrDefault(spawn => !string.IsNullOrWhiteSpace(spawn.Code)) is { } first)
            LocationCode=first.Code;
    }

    public void SetMonsterDetails(MonsterEntry? monster)
    {
        if (monster is not null) IsMvp = monster.IsMvp;
        _monsterImagePath = monster?.LocalImagePath ?? "";
        MonsterImagePng = monster?.LocalImagePng;
        RespawnHints = monster is null ? Array.Empty<MonsterSpawnEntry>() : monster.Spawns;
        SelectFirstLocationIfMissing();
        OnPropertyChanged(nameof(RespawnHints));
        OnPropertyChanged(nameof(MonsterImagePath));
        OnPropertyChanged(nameof(TimerImage));
        OnPropertyChanged(nameof(LocationLabel));
    }

    public void Restart()
    {
        StartFromKillTime(DateTime.Now);
    }

    public MvpTimer CreateRestartDraft(DateTime killedAtUtc)
    {
        var draft = new MvpTimer
        {
            Id = Id, Name = Name, MonsterId = MonsterId, IsMvp = IsMvp, DurationMinutes = DurationMinutes,
            RespawnWindowStartMinutes = RespawnWindowStartMinutes,
            Enabled = Enabled, ShowAfterCompletion = ShowAfterCompletion, LocationCode = LocationCode, DisplayNumber = DisplayNumber,
            CompletionSoundEnabled = CompletionSoundEnabled, CompletionSoundPath = CompletionSoundPath,
            CompletionSoundVolumePercent = CompletionSoundVolumePercent,
            _monsterImagePath = _monsterImagePath, MonsterImagePng = MonsterImagePng, RespawnHints = RespawnHints
        };
        draft.SelectFirstLocationIfMissing();
        draft.StartFromKillTime(killedAtUtc);
        // A new kill needs a new location marker; the live timer is untouched.
        return draft;
    }

    public void ApplyRestartDraft(MvpTimer draft)
    {
        if (draft.Id != Id || draft.KilledAtUtc is null)
            throw new ArgumentException("Restart draft must belong to this timer and contain a kill time.", nameof(draft));
        Name = draft.Name; MonsterId = draft.MonsterId; IsMvp = draft.IsMvp; DurationMinutes = draft.DurationMinutes;
        RespawnWindowStartMinutes = draft.RespawnWindowStartMinutes;
        Enabled = draft.Enabled; ShowAfterCompletion = draft.ShowAfterCompletion; LocationCode = draft.LocationCode; MarkerX = draft.MarkerX; MarkerY = draft.MarkerY;
        CompletionSoundEnabled = draft.CompletionSoundEnabled; CompletionSoundPath = draft.CompletionSoundPath;
        CompletionSoundVolumePercent = draft.CompletionSoundVolumePercent;
        _monsterImagePath = draft._monsterImagePath; MonsterImagePng = draft.MonsterImagePng; RespawnHints = draft.RespawnHints;
        OnPropertyChanged(nameof(MonsterImagePath));
        OnPropertyChanged(nameof(TimerImage));
        OnPropertyChanged(nameof(LocationLabel));
        OnPropertyChanged(nameof(RespawnHints));
        StartFromKillTime(draft.KilledAtUtc.Value);
    }

    public void StartFromKillTime(DateTime killedAtLocal)
    {
        CompletionSoundPlayed = false;
        var local = killedAtLocal.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(killedAtLocal, DateTimeKind.Local)
            : killedAtLocal.ToLocalTime();
        KilledAtUtc = local.ToUniversalTime();
        RespawnWindowOpensAtUtc = RespawnWindowStartMinutes > 0
            ? KilledAtUtc.Value.AddMinutes(DurationMinutes) : null;
        EndsAtUtc = KilledAtUtc.Value.AddMinutes(DurationMinutes + RespawnWindowStartMinutes);
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

    public static string FormatServerUtcOffset(int minutes) =>
        $"UTC{(minutes < 0 ? "−" : "+")}{Math.Abs(minutes) / 60:00}:{Math.Abs(minutes) % 60:00}";

    public void StartFromServerKillClock(TimeSpan killTime, TimeSpan offset, DateTimeOffset? now = null)
    {
        var serverNow = (now ?? DateTimeOffset.UtcNow).ToOffset(offset);
        var killedAt = new DateTimeOffset(serverNow.Date.Add(killTime), offset);
        if (killedAt > serverNow) killedAt = killedAt.AddDays(-1);
        StartFromKillTime(killedAt.UtcDateTime);
    }

    public void Reset()
    {
        CompletionSoundPlayed = true;
        KilledAtUtc = null;
        RespawnWindowOpensAtUtc = null;
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
