using System.ComponentModel;
using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

public sealed record QuestProfileChoice(string Caption, QuestProfileKey? Key);
public sealed record QuestListRow(QuestProgressEntry Progress, QuestGuide? Guide, bool Editable = true)
{
    public string Title => Guide?.Title ?? Progress.Title;
    public string Position => $"[{Progress.CurrentStep}/{Guide?.Steps.Count.ToString() ?? "?"}]";
    public string Instruction => Guide?.Steps.FirstOrDefault(s => s.Number == Progress.CurrentStep)?.Instruction ?? "";
    public bool CanPrev => Editable && !Progress.NeedsStepReview && Guide?.Steps.FirstOrDefault()?.Number < Progress.CurrentStep;
    public bool CanNext => Editable && !Progress.NeedsStepReview && Guide?.Steps.LastOrDefault()?.Number > Progress.CurrentStep;
    public string StateCaption => LocalizationService.Get("QState" + Progress.State);
    public string LastMark => Progress.History.LastOrDefault()?.AtUtc.ToLocalTime().ToString("g") ?? "—";
}

/// <summary>UI-thread controller. Saves immutable copies off-thread; no capture/overlay changes.
/// Calls from capture workers must be dispatched onto the owning UI thread.</summary>
public sealed class QuestWorkspace : INotifyPropertyChanged
{
    public static readonly Guid LocalProfile = Guid.Parse("9e9ce6e7-9a04-4e6e-9dd9-8cc67c932e01");
    private readonly QuestProgressStore _store;
    private readonly QuestCatalogService _reader;
    private readonly SemaphoreSlim _gate = new(1);
    private QuestProgressSession? _session;
    private QuestProfileKey? _desired, _manual;
    private string _server;
    private long _revision, _scopeGeneration;
    private bool _dirty, _switching, _loading;
    private CancellationTokenSource? _catalogCancellation;
    private QuestGuide? _selected;
    public AppSettings Settings { get; }
    public QuestCatalog? Catalog { get; private set; }
    public QuestGuide? SelectedGuide { get => _selected; set { _selected = value; Notify(); } }
    public string Error { get; private set; } = "";
    public string CatalogError { get; private set; } = "";
    public bool IsLoadingCatalog => _loading;
    public bool HasPendingWrites => _dirty;
    public QuestProfileKey? ManualProfile => _manual;
    public bool CanEdit => !_switching && _session is { IsReadOnly: false } && _session.Key == _desired;
    public string ScopeCaption => _desired is null ? LocalizationService.Get("QChooseProfile") :
        (_manual is null ? LocalizationService.Get("QObserved") : LocalizationService.Get("QManual")) + " · " +
        _desired.Server + " · " + (_desired.CharacterId is { } cid ? "CID " + cid : LocalizationService.Get("QLocalProfile"));
    public IReadOnlyList<QuestListRow> Active => Rows.Where(q => q.Progress.State == QuestManualState.Active).ToList();
    public IReadOnlyList<QuestListRow> History => Rows.Where(q => q.Progress.State != QuestManualState.Active).ToList();
    private IEnumerable<QuestListRow> Rows => _session is null || _session.Key != _desired ? [] :
        _session.Document.Quests.OrderBy(q => q.Order).Select(q => new QuestListRow(q, Catalog?.Guides.GetValueOrDefault(q.QuestKey), CanEdit));
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;
    public event Action? SaveRequested;
    public QuestWorkspace(AppSettings settings, QuestProgressStore? store = null, QuestCatalogService? reader = null, QuestCatalog? catalog = null)
    { Settings = settings; _server = settings.ServerProfile; _store = store ?? new(); _reader = reader ?? new(); Catalog = catalog; }

    public async Task LoadCatalogAsync()
    {
        _catalogCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(); _catalogCancellation = cancellation;
        _loading = true; CatalogError = ""; Notify();
        try
        {
            var catalog = await _reader.LoadAsync(cancellation.Token);
            await _gate.WaitAsync(cancellation.Token);
            try
            {
                if (cancellation.IsCancellationRequested) return;
                Catalog = catalog;
                _selected = _selected is null ? null : catalog.Guides.GetValueOrDefault(_selected.Key);
                Reconcile();
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cancellation.IsCancellationRequested) CatalogError = ex.Message; }
        finally
        {
            if (ReferenceEquals(cancellation, _catalogCancellation)) { _catalogCancellation = null; _loading = false; Notify(); }
            cancellation.Dispose();
        }
    }

    public IReadOnlyList<QuestProfileChoice> ProfileChoices()
    {
        var choices = new List<QuestProfileChoice> { new(LocalizationService.Get("QAutoProfile"), null),
            new(LocalizationService.Get("QLocalProfile"), new QuestProfileKey(Settings.ServerProfile, null, LocalProfile)) };
        foreach (var character in Settings.Characters.Where(c => c.CharacterId > 0 &&
                     string.Equals(c.DetectedServer, Settings.ServerProfile, StringComparison.OrdinalIgnoreCase))
                 .GroupBy(c => c.CharacterId).Select(g => g.First()))
            choices.Add(new QuestProfileChoice((string.IsNullOrWhiteSpace(character.Name) ? "CID " + character.CharacterId : character.Name) +
                " · CID " + character.CharacterId, new QuestProfileKey(character.DetectedServer, character.CharacterId, null)));
        try
        {
            foreach (var key in _store.ListKnownProfiles(Settings.ServerProfile).Where(k => !choices.Any(c => c.Key == k)))
                choices.Add(new QuestProfileChoice(key.CharacterId is { } cid ? "CID " + cid :
                    LocalizationService.Get("QLocalProfile") + " · " + key.LocalProfileId?.ToString("N")[..8], key));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = ex.Message; }
        return choices;
    }

    public Task SelectProfileAsync(QuestProfileKey? manual, CharacterSessionSnapshot observed)
    {
        if (manual is not null && !string.Equals(manual.Server, Settings.ServerProfile, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Profile server mismatch.");
        // This is a fresh explicit choice for the currently selected server,
        // not the previous server's manual selection that Refresh must clear.
        _server = Settings.ServerProfile;
        _manual = manual;
        return RefreshIdentityAsync(observed);
    }

    public async Task RefreshIdentityAsync(CharacterSessionSnapshot observed)
    {
        if (_server != Settings.ServerProfile) { _server = Settings.ServerProfile; _manual = null; }
        var key = _manual ?? QuestProfileKey.FromObserved(observed, Settings.ServerProfile, Settings.PacketCaptureEnabled);
        _desired = key;
        if (_session?.Key == key && !_switching) { Notify(); return; }
        var generation = ++_scopeGeneration;
        _switching = true; Notify();
        await _gate.WaitAsync();
        try
        {
            if (generation != _scopeGeneration) return;
            if (!await SaveCoreAsync()) return; // retain unsaved old document, never charge it to the new identity
            _session = null; Notify();
            if (key is not null)
            {
                var loaded = await Task.Run(() => _store.Load(key));
                if (generation != _scopeGeneration) return;
                _session = loaded;
                Error = loaded.RecoveryWarning is null ? "" : LocalizationService.Get("QRecovery");
                Reconcile();
            }
            else Error = "";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { _gate.Release(); if (generation == _scopeGeneration) _switching = false; Notify(); }
    }

    private void Reconcile()
    {
        if (_session is null || Catalog is null) return;
        var before = JsonSerializer.Serialize(_session.Document);
        QuestProgress.Reconcile(_session.Document, Catalog);
        if (!_session.IsReadOnly && before != JsonSerializer.Serialize(_session.Document)) MarkDirty();
    }
    public void AddSelected()
    {
        if (!CanEdit || SelectedGuide is null || SelectedGuide.Steps.Count == 0) return;
        var existing = _session!.Document.Quests.Any(q => q.QuestKey == SelectedGuide.Key);
        QuestProgress.Add(_session.Document, SelectedGuide);
        if (!existing) MarkDirty(); else Notify();
    }
    public bool IsActive(QuestGuide? guide) => guide is not null && Rows.Any(q => q.Progress.QuestKey == guide.Key && q.Progress.State == QuestManualState.Active);
    public bool HasProgress(QuestGuide? guide) => guide is not null && Rows.Any(q => q.Progress.QuestKey == guide.Key);
    public void Move(QuestListRow row, int direction)
    { if (CanEdit && Owns(row) && row.Guide is not null && QuestProgress.Move(row.Progress, row.Guide, direction)) MarkDirty(); }
    public void SelectStep(int number)
    {
        var entry = _session?.Document.Quests.FirstOrDefault(q => q.QuestKey == SelectedGuide?.Key);
        if (!CanEdit || entry is null || SelectedGuide is null) return;
        QuestProgress.SelectStep(entry, SelectedGuide, number); MarkDirty();
    }
    public bool IsCurrentStep(int number) => _session?.Key == _desired &&
        _session?.Document.Quests.Any(q => q.QuestKey == SelectedGuide?.Key && q.CurrentStep == number) == true;
    public void SetState(QuestListRow row, QuestManualState state, bool confirmed = false)
    { if (CanEdit && Owns(row)) { QuestProgress.SetState(row.Progress, state, DateTime.UtcNow, confirmed); MarkDirty(); } }
    public void SetVisibility(QuestListRow row, bool visible)
    { if (CanEdit && Owns(row)) { row.Progress.IsVisibleInOverlay = visible; MarkDirty(); } }
    public void Reorder(QuestListRow row, int direction)
    {
        if (!CanEdit || !Owns(row)) return;
        var rows = Active.ToList(); var index = rows.FindIndex(q => q.Progress == row.Progress);
        if (index < 0 || index + direction < 0 || index + direction >= rows.Count) return;
        (rows[index], rows[index + direction]) = (rows[index + direction], rows[index]);
        for (var i = 0; i < rows.Count; i++) rows[i].Progress.Order = i;
        MarkDirty();
    }
    private bool Owns(QuestListRow row) => _session?.Document.Quests.Contains(row.Progress) == true;
    private void MarkDirty() { _revision++; _dirty = true; Notify(); SaveRequested?.Invoke(); }
    public void RefreshLanguage() => Notify();
    private void Notify() { PropertyChanged?.Invoke(this, new("")); Changed?.Invoke(); }

    public async Task<bool> FlushAsync()
    {
        await _gate.WaitAsync();
        try { return await SaveCoreAsync(); }
        finally { _gate.Release(); Notify(); }
    }
    public async Task<bool> SaveAndRefreshAsync(CharacterSessionSnapshot observed)
    {
        if (!await FlushAsync()) return false;
        await RefreshIdentityAsync(observed);
        return !HasPendingWrites;
    }
    private async Task<bool> SaveCoreAsync()
    {
        if (!_dirty || _session is null) return true;
        var session = _session; var revision = _revision;
        try
        {
            var copy = JsonSerializer.Deserialize<QuestProgressDocument>(JsonSerializer.Serialize(session.Document))!;
            var write = new QuestProgressSession(session.Key, copy, session.DiskHash);
            await Task.Run(() => _store.Save(write));
            session.DiskHash = write.DiskHash;
            if (_session == session && revision == _revision) _dirty = false;
            Error = "";
            return true;
        }
        catch (Exception ex) { Error = LocalizationService.Get("QSaveError") + " " + ex.Message; return false; }
    }
    public void CancelCatalogLoad() { _catalogCancellation?.Cancel(); _catalogCancellation = null; _loading = false; }
}
