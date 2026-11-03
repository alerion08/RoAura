using System.Windows;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class App
{
    private FarmingSessionHistoryStore? _farmingHistory;
    private readonly Dictionary<Guid, FarmingSession> _farmingSessions = [];
    private bool _historyWarningShown;
    public bool FlushFarmingHistory() => SaveFarmingHistory(flush: true);
    public FarmingSessionHistoryStore FarmingHistory => _farmingHistory ??= new();

    public bool ToggleFarmingSessionPause()
    {
        var record = CurrentCharacterStatistics();
        if (record is null || _characterLedger is null || _settingsWindow is null) return false;
        var paused = !record.SessionPaused;
        if (!SafeCharacterStatisticsValue(() => _characterLedger.SetPaused(paused, DateTime.UtcNow))) return false;
        _settingsWindow.Settings.SessionPaused = paused;
        _overlay?.RefreshCharacterStatistics();
        SaveFarmingHistory();
        return true;
    }

    private FarmingSession? GetFarmingSession(bool allowLastActive = false)
    {
        var record = CurrentCharacterStatistics();
        if (record is null && allowLastActive && _characterLedger?.Active is { } key)
            record = _characterLedger.Snapshot(key, DateTime.UtcNow);
        if (record is null || (!record.SessionTimerStarted && record.GoldGoblinRows.Count == 0 &&
            record.SessionLoot.Count == 0 && record.SessionZenyGained == 0)) return null;
        if (!_farmingSessions.TryGetValue(record.SessionId, out var history))
        {
            history = FarmingHistory.Read(record.SessionId) ?? new FarmingSession
            {
                Id = record.SessionId, Server = record.ServerKey, CharacterId = record.CharacterId,
                StartedUtc = record.SessionStartedUtc == default ? DateTime.UtcNow : record.SessionStartedUtc
            };
            _farmingSessions.Add(record.SessionId, history);
        }
        var name = _settingsWindow?.Settings.Characters.FirstOrDefault(character =>
            character.CharacterId == record.CharacterId &&
            character.DetectedServer.Equals(record.ServerKey, StringComparison.OrdinalIgnoreCase))?.Name;
        if (!string.IsNullOrWhiteSpace(name)) history.CharacterName = name;
        else if (string.IsNullOrWhiteSpace(history.CharacterName)) history.CharacterName = $"CID {record.CharacterId}";
        history.ActiveTicks = record.SessionActiveTicks;
        history.ZenyGained = record.SessionZenyGained;
        history.UpdatedUtc = DateTime.UtcNow;
        return history;
    }

    private void RecordFarmingLoot(int id, string name, long count, long price, string source, bool known, bool excluded)
    {
        try
        {
            if (GetFarmingSession() is not { } session) return;
            session.RecordLoot(DateTime.UtcNow, id, name, count, price, source, known, excluded);
            FarmingHistory.Queue(session);
        }
        catch (Exception ex) { ReportHistoryError(ex); }
    }

    private bool SaveFarmingHistory(bool complete = false, bool flush = false)
    {
        try
        {
            if (GetFarmingSession(allowLastActive: true) is { } session)
            {
                if (complete) session.EndedUtc = DateTime.UtcNow;
                FarmingHistory.Queue(session);
            }
            if (flush) _farmingHistory?.Flush();
            return true;
        }
        catch (Exception ex) { ReportHistoryError(ex); return false; }
    }
    private void ReportHistoryError(Exception ex)
    {
        DebugLog.Write($"FARMING SESSION HISTORY ERROR: {ex}");
        if (_historyWarningShown) return;
        _historyWarningShown = true;
        MessageBox.Show(_settingsWindow,
            LocalizationService.Get("SessionHistoryError") + "\n" + ex.Message,
            "RoAura", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
