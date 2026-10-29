using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>Local-only history, independent of portable packages and settings/presets.</summary>
public sealed class MarketWatchHistoryStore
{
    public const string FileName = "market-watch-history.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string FilePath { get; }
    public bool HasError { get; private set; }
    private bool _canWrite = true;

    public MarketWatchHistoryStore(string? filePath = null) => FilePath = filePath ?? Path.Combine(AppEnvironment.LocalDirectory, FileName);

    public IReadOnlyList<MarketWatchHistoryEntry> Load()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var document = JsonSerializer.Deserialize<HistoryDocument>(File.ReadAllText(FilePath));
            if (document is null || document.Version != 1 || document.Entries is null)
                throw new InvalidDataException("Unsupported or invalid market history document.");
            HasError = false;
            return document.Entries.Where(e => e is not null && e.ItemId > 0 && e.Price >= 0 &&
                e.DetectedAtUtc > DateTime.MinValue).ToList();
        }
        catch (Exception ex)
        {
            // Do not overwrite a damaged/unreadable/newer file with an empty history.
            // Keep matching in memory and report the persistence failure in the UI.
            HasError = true;
            _canWrite = false;
            DebugLog.Write($"MARKET WATCH HISTORY LOAD ERROR: {ex}");
            return [];
        }
    }

    public bool Save(IReadOnlyList<MarketWatchHistoryEntry> entries)
    {
        if (!_canWrite) return false;
        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new HistoryDocument { Entries = entries.ToList() }, Options));
            File.Move(temporary, FilePath, overwrite: true);
            HasError = false;
            return true;
        }
        catch (Exception ex)
        {
            HasError = true;
            DebugLog.Write($"MARKET WATCH HISTORY SAVE ERROR: {ex}");
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) { DebugLog.Write($"MARKET WATCH HISTORY TEMP CLEANUP ERROR: {ex.Message}"); }
        }
    }

    private sealed class HistoryDocument
    {
        public HistoryDocument() { }
        public int Version { get; set; } = 1;
        public List<MarketWatchHistoryEntry> Entries { get; set; } = [];
    }
}
