using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class MarketWatchHistoryEntry
{
    public DateTime DetectedAtUtc { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string Stall { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Map { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public long Price { get; set; }
    public string Refine { get; set; } = "";
    public string Cards { get; set; } = "";
    [JsonIgnore] public string TimeCaption => DetectedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    [JsonIgnore] public string ShopCaption => string.IsNullOrWhiteSpace(Stall) ? Owner : Stall;
    [JsonIgnore] public string Coordinates => $"{Map} ({X},{Y})";
    [JsonIgnore] public string PriceCaption => $"{Price:N0} Zeny";
    // Numeric price, rather than the site's formatted string, is part of the identity.
    // Quantity is deliberately excluded: selling some of an existing stack is not a new offer.
    [JsonIgnore] public string Key => System.Text.Json.JsonSerializer.Serialize(new object[]
        { ItemId, Owner, Stall, Map, X, Y, Refine, Cards, Price });
}
