namespace RoAura.Services;

/// <summary>Persistent identity uses the logical game server and Character ID, never the TCP port.</summary>
public readonly record struct CharacterStatisticsKey(string ServerKey, uint CharacterId)
{
    public static CharacterStatisticsKey Create(string serverKey, uint characterId)
    {
        var normalized = (serverKey ?? "").Trim().ToUpperInvariant();
        if (characterId == 0 || normalized.Length is < 1 or > 48 ||
            normalized.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_')))
            throw new ArgumentException("A valid server identifier and nonzero Character ID are required.");
        return new CharacterStatisticsKey(normalized, characterId);
    }
}

public sealed class CharacterLootCount
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public long Count { get; set; }
}

/// <summary>
/// Only numeric farming data. Pricing rules, overlay coordinates, effects and presets
/// deliberately remain in their existing shared configuration.
/// </summary>
public sealed class CharacterStatisticsDocument
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public string ServerKey { get; set; } = "";
    public uint CharacterId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
    public DateTime SessionStartedUtc { get; set; }

    public long LifetimeBaseExperience { get; set; }
    public long LifetimeJobExperience { get; set; }
    public long LifetimeZenyGained { get; set; }
    public long LifetimeActiveTicks { get; set; }
    public long SessionBaseExperience { get; set; }
    public long SessionJobExperience { get; set; }
    public long SessionZenyGained { get; set; }
    public long SessionActiveTicks { get; set; }
    public ulong CurrentBaseExperience { get; set; }
    public ulong NextBaseExperience { get; set; }
    public ulong CurrentJobExperience { get; set; }
    public ulong NextJobExperience { get; set; }
    public Dictionary<int, CharacterLootCount> LifetimeLoot { get; set; } = [];
    public Dictionary<int, CharacterLootCount> SessionLoot { get; set; } = [];

    public CharacterStatisticsDocument Copy() => new()
    {
        SchemaVersion = SchemaVersion,
        ServerKey = ServerKey,
        CharacterId = CharacterId,
        CreatedUtc = CreatedUtc,
        LastUpdatedUtc = LastUpdatedUtc,
        SessionStartedUtc = SessionStartedUtc,
        LifetimeBaseExperience = LifetimeBaseExperience,
        LifetimeJobExperience = LifetimeJobExperience,
        LifetimeZenyGained = LifetimeZenyGained,
        LifetimeActiveTicks = LifetimeActiveTicks,
        SessionBaseExperience = SessionBaseExperience,
        SessionJobExperience = SessionJobExperience,
        SessionZenyGained = SessionZenyGained,
        SessionActiveTicks = SessionActiveTicks,
        CurrentBaseExperience = CurrentBaseExperience,
        NextBaseExperience = NextBaseExperience,
        CurrentJobExperience = CurrentJobExperience,
        NextJobExperience = NextJobExperience,
        LifetimeLoot = LifetimeLoot.ToDictionary(p => p.Key, p => new CharacterLootCount
            { ItemId = p.Value.ItemId, Name = p.Value.Name, Count = p.Value.Count }),
        SessionLoot = SessionLoot.ToDictionary(p => p.Key, p => new CharacterLootCount
            { ItemId = p.Value.ItemId, Name = p.Value.Name, Count = p.Value.Count })
    };

    public double SessionActiveHours => TimeSpan.FromTicks(SessionActiveTicks).TotalHours;
    public double BaseExperiencePerHour =>
        SessionActiveHours <= 0 ? 0 : SessionBaseExperience / SessionActiveHours;
    public double JobExperiencePerHour =>
        SessionActiveHours <= 0 ? 0 : SessionJobExperience / SessionActiveHours;
    public double ZenyPerHour =>
        SessionActiveHours <= 0 ? 0 : SessionZenyGained / SessionActiveHours;
}
