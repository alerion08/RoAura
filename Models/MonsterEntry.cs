namespace RoAura.Models;

public sealed class MonsterEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NameRu { get; set; } = "";
    public bool IsMvp { get; set; } = true;
    public string DisplayName => string.IsNullOrWhiteSpace(NameRu) ? $"{Name} ({Id})" : $"{Name} / {NameRu} ({Id})";
}
