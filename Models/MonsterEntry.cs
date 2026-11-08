using System.Text.Json.Serialization;

namespace RoAura.Models;

public sealed class MonsterEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NameRu { get; set; } = "";
    [JsonIgnore] public string LocalImagePath { get; set; } = "";
    [JsonIgnore] public bool IsReference { get; set; }
    public bool IsMvp { get; set; } = true;
    public List<MonsterSpawnEntry> Spawns { get; set; } = [];
    public string DisplayName => string.IsNullOrWhiteSpace(NameRu) ? $"{Name} ({Id})" : $"{Name} / {NameRu} ({Id})";
    public override string ToString() => DisplayName;
    public bool MatchesTimerSearch(bool isMvp, string query) =>
        IsMvp == isMvp && (query.Length == 0 ||
            Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            NameRu.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public string ImagePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LocalImagePath)) return LocalImagePath;
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "MvpImages", $"{Id}.png");
            return File.Exists(path) ? path : "";
        }
    }
}

public sealed class MonsterSpawnEntry
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Count { get; set; } = "";
    public string Respawn { get; set; } = "";
}
