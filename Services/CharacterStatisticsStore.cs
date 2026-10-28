using System.Text;
using System.Text.Json;

namespace RoAura.Services;

/// <summary>
/// A versioned, per-character local store. Atomic same-directory replacement keeps
/// the last valid document as a backup. Invalid/newer documents are NEVER overwritten.
/// No migration or copying of the stable 3.0.0 settings is performed.
/// </summary>
public sealed class CharacterStatisticsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string RootDirectory { get; }

    public CharacterStatisticsStore(string? rootDirectory = null) =>
        RootDirectory = rootDirectory ?? Path.Combine(AppEnvironment.LocalDirectory, "CharacterStatistics");

    public string PathFor(CharacterStatisticsKey key)
    {
        var normalized = CharacterStatisticsKey.Create(key.ServerKey, key.CharacterId);
        return Path.Combine(RootDirectory, normalized.ServerKey,
            $"character-{normalized.CharacterId}.json");
    }

    public CharacterStatisticsDocument? Load(CharacterStatisticsKey key)
    {
        var path = PathFor(key);
        var backup = path + ".bak";
        if (!File.Exists(path) && !File.Exists(backup)) return null;
        try
        {
            return ReadValid(path, key);
        }
        catch (Exception primaryError) when (primaryError is IOException or JsonException or ArgumentException)
        {
            // A recovery copy may be inspected, but Save() will refuse to touch
            // its damaged primary file until the problem is explicitly resolved.
            if (File.Exists(backup))
            {
                try { return ReadValid(backup, key); }
                catch (Exception recoveryError) when (recoveryError is IOException or JsonException or ArgumentException)
                {
                    throw new InvalidDataException($"Character statistics and backup are invalid: {path}", recoveryError);
                }
            }
            throw new InvalidDataException($"Character statistics cannot be loaded: {path}", primaryError);
        }
    }

    public void Save(CharacterStatisticsDocument document)
    {
        var key = CharacterStatisticsKey.Create(document.ServerKey, document.CharacterId);
        Validate(document, key);
        var path = PathFor(key);
        var backup = path + ".bak";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Refuse to replace unreadable, corrupt or newer data; do not silently
        // erase its history even when a valid backup has been loaded.
        if (File.Exists(path)) _ = ReadValid(path, key);
        else if (File.Exists(backup))
            throw new InvalidDataException($"Statistics primary file is missing, backup preserved: {path}");

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, backup);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static CharacterStatisticsDocument ReadValid(string path, CharacterStatisticsKey expected)
    {
        var content = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<CharacterStatisticsDocument>(content);
        if (document is null) throw new InvalidDataException("Empty statistics document.");
        using (var json = JsonDocument.Parse(content))
        {
            // Backward compatibility with pre-shared-clock DEV documents.
            if (!json.RootElement.TryGetProperty(nameof(CharacterStatisticsDocument.SessionTimerStarted), out _) &&
                (document.SessionBaseExperience > 0 || document.SessionJobExperience > 0))
                document.SessionTimerStarted = true;
        }
        Validate(document, expected);
        return document;
    }

    private static void Validate(CharacterStatisticsDocument data, CharacterStatisticsKey expected)
    {
        var key = CharacterStatisticsKey.Create(data.ServerKey, data.CharacterId);
        if (key != CharacterStatisticsKey.Create(expected.ServerKey, expected.CharacterId))
            throw new InvalidDataException("Character statistics identity mismatch.");
        if (data.SchemaVersion != CharacterStatisticsDocument.CurrentSchema)
            throw new InvalidDataException("Unsupported or newer character statistics schema.");
        if (data.LifetimeBaseExperience < 0 || data.LifetimeJobExperience < 0 ||
            data.LifetimeZenyGained < 0 || data.LifetimeActiveTicks < 0 ||
            data.SessionBaseExperience < 0 || data.SessionJobExperience < 0 ||
            data.SessionZenyGained < 0 || data.SessionActiveTicks < 0 ||
            data.SessionBaseExperience > data.LifetimeBaseExperience ||
            data.SessionJobExperience > data.LifetimeJobExperience ||
            data.SessionZenyGained > data.LifetimeZenyGained ||
            data.SessionActiveTicks > data.LifetimeActiveTicks)
            throw new InvalidDataException("Negative or contradictory statistics values.");
        if (data.LifetimeLoot is null || data.SessionLoot is null ||
            data.LifetimeLoot.Count > 50000 || data.SessionLoot.Count > 50000 ||
            data.GoldGoblinRows is null || data.GoldGoblinRows.Count > 50000)
            throw new InvalidDataException("Invalid or oversized loot collection.");
        foreach (var (itemId, entry) in data.LifetimeLoot.Concat(data.SessionLoot).Concat(data.GoldGoblinRows))
            if (itemId <= 0 || entry is null || entry.ItemId != itemId ||
                entry.Count < 0 || entry.ManualUnitCost < 0 || entry.Name is null || entry.Name.Length > 500)
                throw new InvalidDataException("Invalid loot entry.");
    }
}
