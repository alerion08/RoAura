using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class FarmingLootEntry
{
    public DateTime ReceivedUtc { get; set; }
    public DateTime LastReceivedUtc { get; set; }
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public long Count { get; set; }
    public long UnitPrice { get; set; }
    public string PriceSource { get; set; } = "";
    public bool PriceKnown { get; set; }
    public bool Excluded { get; set; }
    [JsonIgnore] public long Total => Excluded ? 0 : checked(Count * UnitPrice);
    [JsonIgnore] public string TimeText => ReceivedUtc.ToLocalTime().ToString("G") +
        (LastReceivedUtc > ReceivedUtc ? " — " + LastReceivedUtc.ToLocalTime().ToString("T") : "");
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
    public void RecordLoot(DateTime receivedUtc, int id, string name, long count, long price, string source, bool known, bool excluded)
    {
        // A price bucket retains its original valuation while bounding long-session growth.
        var row = Loot.LastOrDefault(row => row.ItemId == id && row.UnitPrice == price &&
            row.PriceSource == source && row.PriceKnown == known && row.Excluded == excluded && row.Name == name);
        if (row is not null) { row.Count = checked(row.Count + count); row.LastReceivedUtc = receivedUtc; }
        else Loot.Add(new FarmingLootEntry { ReceivedUtc = receivedUtc, LastReceivedUtc = receivedUtc,
            ItemId = id, Name = name, Count = count, UnitPrice = price, PriceSource = source,
            PriceKnown = known, Excluded = excluded });
    }
    [JsonIgnore] public long ItemCount => Loot.Where(row => !row.Excluded).Sum(row => row.Count);
    [JsonIgnore] public long LootValue => Loot.Sum(row => row.Total);
    [JsonIgnore] public string DateText => StartedUtc.ToLocalTime().ToString("G");
    [JsonIgnore] public string DurationText => TimeSpan.FromTicks(ActiveTicks).ToString(@"dd\.hh\:mm\:ss");
    [JsonIgnore] public string CharacterCaption => $"{CharacterName} · {Server} · CID {CharacterId}";
}
