using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>One atomic local file per session; deletions also cancel queued writes.</summary>
public sealed class FarmingSessionHistoryStore
{
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, FarmingSession> _pending = [];
    private readonly HashSet<Guid> _deleted;
    private bool _writing;
    private Task _writer = Task.CompletedTask;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string? LastError { get; private set; }
    public FarmingSessionHistoryStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(AppEnvironment.LocalDirectory, "GoldGoblinSessions");
        var index = Path.Combine(_directory, "deleted.json");
        _deleted = File.Exists(index) ? JsonSerializer.Deserialize<HashSet<Guid>>(File.ReadAllText(index)) ??
            throw new InvalidDataException("Invalid deleted-session index.") : [];
    }
    private string FilePath(Guid id) => Path.Combine(_directory, id.ToString("N") + ".json");
    public FarmingSession? Read(Guid id)
    {
        lock (_gate)
        {
            if (_deleted.Contains(id)) return null;
            if (_pending.TryGetValue(id, out var queued)) return Copy(queued);
            var path = FilePath(id);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Session file is too large.");
            var value = JsonSerializer.Deserialize<FarmingSession>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException("Empty session.");
            Validate(value);
            if (value.Id != id) throw new InvalidDataException("Session identity mismatch.");
            return value;
        }
    }
    public IReadOnlyList<FarmingSession> List()
    {
        Flush();
        var rows = new List<FarmingSession>();
        if (!Directory.Exists(_directory)) return rows;
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)) continue;
            try { if (Read(id) is { } row) rows.Add(row); }
            catch (Exception ex) { DebugLog.Write($"SESSION HISTORY READ ERROR: {ex}"); }
        }
        return rows.OrderByDescending(row => row.StartedUtc).ToList();
    }
    public void Queue(FarmingSession session)
    {
        Validate(session);
        var snapshot = Copy(session);
        lock (_gate)
        {
            if (_deleted.Contains(session.Id)) return;
            _pending[session.Id] = snapshot;
            if (!_writing) { _writing = true; _writer = Task.Run(async () => { await Task.Delay(500); Drain(); }); }
        }
    }
    public void Flush()
    {
        Task writer;
        lock (_gate) writer = _writer;
        writer.GetAwaiter().GetResult();
        if (LastError is not null) throw new IOException(LastError);
    }
    private void Drain()
    {
        lock (_gate)
        {
            try
            {
                foreach (var pair in _pending)
                    if (!_deleted.Contains(pair.Key)) WriteAtomic(FilePath(pair.Key), pair.Value);
                _pending.Clear();
                LastError = null;
            }
            catch (Exception ex) { LastError = ex.Message; DebugLog.Write($"SESSION HISTORY SAVE ERROR: {ex}"); }
            finally { _writing = false; }
        }
    }
    public void SetExpenses(Guid id, long expenses)
    {
        lock (_gate)
        {
            var session = Read(id) ?? throw new InvalidOperationException("Session no longer exists.");
            session.ExpensesZeny = expenses;
            Validate(session);
            WriteAtomic(FilePath(id), session);
            _pending.Remove(id);
        }
    }

    public void SetNote(Guid id, string note, string? tags = null)
    {
        lock (_gate)
        {
            var session = Read(id) ?? throw new InvalidOperationException("Session no longer exists.");
            session.Note = note;
            if (tags is not null) session.Tags = tags;
            Validate(session);
            WriteAtomic(FilePath(id), session);
            _pending.Remove(id);
        }
    }

    public void Delete(Guid id)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var next = new HashSet<Guid>(_deleted) { id };
            WriteAtomic(Path.Combine(_directory, "deleted.json"), next);
            _deleted.Add(id);
            _pending.Remove(id);
            File.Delete(FilePath(id));
        }
    }
    private static FarmingSession Copy(FarmingSession value) =>
        JsonSerializer.Deserialize<FarmingSession>(JsonSerializer.Serialize(value, Options), Options)!;
    private static void Validate(FarmingSession value)
    {
        if (value.SchemaVersion != 1 || value.Id == Guid.Empty || value.CharacterId == 0 ||
            value.StartedUtc.Kind != DateTimeKind.Utc || value.ActiveTicks < 0 || value.ZenyGained < 0 || value.ExpensesZeny < 0 ||
            value.Note is null || value.Note.Length > 500 || value.Tags is null || value.Tags.Length > 200 || value.CharacterName.Length > 500 || value.Loot is null || value.Loot.Count > 100000 ||
            value.Loot.Any(row => row.ItemId <= 0 || row.Count <= 0 || row.UnitPrice < 0 ||
                row.Name.Length > 500 || row.ReceivedUtc.Kind != DateTimeKind.Utc))
            throw new InvalidDataException("Invalid or unsupported session history.");
        if (value.Intervals is null || value.Intervals.Any(pair => pair.Key is < 0 or > long.MaxValue / FarmingInterval.Ticks || !pair.Value.IsValid))
            throw new InvalidDataException("Invalid farming intervals.");
        if (value.Plan is { } plan &&
            (plan.PriceSource is not ("NPC" or "BUY" or "SELL" or "AUTO") ||
             plan.Targets is null || plan.Targets.Length == 0 ||
             plan.Targets.Any(row => row is null || row.MonsterId <= 0 || row.Kills <= 0) ||
             plan.Drops is null ||
             plan.Drops.Any(row => row is null || row.ItemId <= 0 || row.Quantity < 0 || row.UnitPrice < 0) ||
             plan.Drops.Select(row => row.ItemId).Distinct().Count() != plan.Drops.Length))
            throw new InvalidDataException("Invalid farming plan.");
        CharacterStatisticsKey.Create(value.Server, value.CharacterId);
        _ = value.LootValue; _ = value.ItemCount;
    }
    private static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, value, Options);
                file.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
