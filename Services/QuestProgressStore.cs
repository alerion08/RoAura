using System.Security.Cryptography;
using System.Text.Json;

namespace RoAura.Services;

public sealed class QuestProgressSession
{
    public QuestProgressDocument Document { get; }
    public bool IsReadOnly { get; }
    public string? RecoveryWarning { get; }
    internal QuestProfileKey Key { get; }
    internal string? DiskHash { get; set; }
    internal QuestProgressSession(QuestProfileKey key, QuestProgressDocument document, string? hash, string? warning = null)
    {
        Key = key; Document = document; DiskHash = hash;
        RecoveryWarning = warning; IsReadOnly = warning is not null;
    }
}

/// <summary>Separate from settings/presets and the read-only source catalog.
/// First format is v1: no preexisting quest state is guessed or imported.</summary>
public sealed class QuestProgressStore
{
    private const int MaxBytes = 16 * 1024 * 1024;
    private readonly string _root;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public QuestProgressStore(string? root = null) => _root = root ?? Path.Combine(AppEnvironment.LocalDirectory, "ROQuestie");
    public string PathFor(QuestProfileKey key) => Path.Combine(_root, key.Server, key.StorageId + ".json");

    public IReadOnlyList<QuestProfileKey> ListKnownProfiles(string server)
    {
        var canonicalServer = new QuestProfileKey(server, 1, null).Server;
        var directory = Path.Combine(_root, canonicalServer);
        if (!Directory.Exists(directory)) return [];
        var keys = new HashSet<QuestProfileKey>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json*"))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".bak", StringComparison.Ordinal)) name = name[..^4];
            if (!name.EndsWith(".json", StringComparison.Ordinal)) continue;
            name = name[..^5];
            if (name.StartsWith("cid-", StringComparison.Ordinal) && uint.TryParse(name[4..], out var cid) && cid > 0)
                keys.Add(new QuestProfileKey(canonicalServer, cid, null));
            if (name.StartsWith("local-", StringComparison.Ordinal) && Guid.TryParseExact(name[6..], "N", out var local) && local != Guid.Empty)
                keys.Add(new QuestProfileKey(canonicalServer, null, local));
        }
        return keys.OrderBy(k => k.StorageId, StringComparer.Ordinal).ToList();
    }

    public QuestProgressSession Load(QuestProfileKey key)
    {
        var path = PathFor(key);
        if (!File.Exists(path) && !File.Exists(path + ".bak"))
            return new QuestProgressSession(key, QuestProgressDocument.Create(key), null);
        try
        {
            var (data, hash) = Read(path, key);
            return new QuestProgressSession(key, data, hash);
        }
        catch (Exception ex) when (Recoverable(ex))
        {
            try
            {
                var (data, _) = Read(path + ".bak", key);
                return new QuestProgressSession(key, data, null, "Backup opened read-only; primary file must be recovered explicitly.");
            }
            catch (Exception backup) when (Recoverable(backup))
            { throw new InvalidDataException("Quest progress and backup cannot be loaded; original files preserved.", ex); }
        }
    }

    public void Save(QuestProgressSession session)
    {
        if (session.IsReadOnly) throw new InvalidDataException("Recovered progress is read-only; damaged/newer primary preserved.");
        Validate(session.Document, session.Key);
        var path = PathFor(session.Key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Serialize competing writers, then reject stale snapshots rather than lose progress.
        using var exclusive = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path))
        {
            var (_, hash) = Read(path, session.Key); // refuses corrupt and future schemas
            if (hash != session.DiskHash) throw new IOException("Quest progress changed on disk; reload before saving.");
        }
        else if (session.DiskHash is not null || File.Exists(path + ".bak"))
            throw new IOException("Quest progress primary is missing; recovery files preserved.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session.Document, Options);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Quest progress size limit exceeded.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(flushToDisk: true); }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            session.DiskHash = Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static (QuestProgressDocument Document, string Hash) Read(string path, QuestProfileKey key)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Quest progress file too large.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        using var json = JsonDocument.Parse(bytes);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty(nameof(QuestProgressDocument.SchemaVersion), out var schema) ||
            schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt32(out var version) || version != QuestProgressDocument.CurrentSchema)
            throw new InvalidDataException("Unsupported quest progress schema; no implicit migration.");
        var document = JsonSerializer.Deserialize<QuestProgressDocument>(bytes)
            ?? throw new InvalidDataException("Empty quest progress document.");
        Validate(document, key);
        return (document, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static bool Recoverable(Exception ex) => ex is IOException or InvalidDataException or JsonException or ArgumentException;
    private static void Validate(QuestProgressDocument document, QuestProfileKey key)
    {
        if (document.SchemaVersion != QuestProgressDocument.CurrentSchema ||
            new QuestProfileKey(document.Server, document.CharacterId, document.LocalProfileId) != key ||
            document.Quests is null || document.Quests.Count > 10_000)
            throw new InvalidDataException("Invalid progress identity, schema or quest collection.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in document.Quests)
        {
            if (q is null || string.IsNullOrWhiteSpace(q.QuestKey) || q.QuestKey.Length > 2048 || !keys.Add(q.QuestKey) ||
                q.Title is null || q.Title.Length > 8192 || q.CurrentStep < 1 || q.Order < 0 || !Enum.IsDefined(q.State) ||
                (q.StepAnchor is not null && (q.StepAnchor.Length != 64 || q.StepAnchor.Any(c => !Uri.IsHexDigit(c)))) ||
                q.CatalogContentHash?.Length > 256 || q.History is null || q.History.Count > 20_000 ||
                q.History.Any(h => h is null || !Enum.IsDefined(h.State) || h.Method != "manual" || h.AtUtc.Kind != DateTimeKind.Utc))
                throw new InvalidDataException("Invalid or duplicate quest progress entry.");
        }
    }
}
