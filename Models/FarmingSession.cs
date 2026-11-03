using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class FarmingLootEntry
{
    public DateTime ReceivedUtc { get; set; }
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public long Count { get; set; }
    public long UnitPrice { get; set; }
    public string PriceSource { get; set; } = "";
    public bool PriceKnown { get; set; }
    public bool Excluded { get; set; }
    [JsonIgnore] public long Total => Excluded ? 0 : checked(Count * UnitPrice);
    [JsonIgnore] public string TimeText => ReceivedUtc.ToLocalTime().ToString("G");
}
public sealed class FarmingSession
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; }
    public string Server { get; set; } = "";
    public uint CharacterId { get; set; }
    public string CharacterName { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public long ActiveTicks { get; set; }
    public long ZenyGained { get; set; }
    public List<FarmingLootEntry> Loot { get; set; } = [];
    [JsonIgnore] public long ItemCount => Loot.Where(row => !row.Excluded).Sum(row => row.Count);
    [JsonIgnore] public long LootValue => Loot.Sum(row => row.Total);
    [JsonIgnore] public string DateText => StartedUtc.ToLocalTime().ToString("G");
    [JsonIgnore] public string DurationText => TimeSpan.FromTicks(ActiveTicks).ToString(@"dd\.hh\:mm\:ss");
    [JsonIgnore] public string CharacterCaption => $"{CharacterName} · {Server} · CID {CharacterId}";
}
