namespace RoAura.Models;

public sealed record DatabaseFavorite(DatabaseSearchKind Kind, string Key, string Name)
{
    public string Note { get; set; } = "";
}

public sealed class DatabaseFavoriteList
{
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "";
    public List<DatabaseFavorite> Entries { get; set; } = [];
}
