using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class ItemEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Loot";
    [JsonIgnore] public string ExactName { get; set; } = "";
    [JsonIgnore] public string FullName => string.IsNullOrWhiteSpace(ExactName) ? Name : ExactName;
}
