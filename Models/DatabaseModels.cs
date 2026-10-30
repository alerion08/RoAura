namespace RoAura.Models;

public sealed class GameDatabase
{
    public string Source { get; set; } = "SpringRO";
    public List<DatabaseMonster> Monsters { get; set; } = [];
    public List<DatabaseItem> Items { get; set; } = [];
    public List<DatabaseMap> Maps { get; set; } = [];
}

public sealed class DatabaseMonster
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsMvp { get; set; }
    public bool IsBoss { get; set; }
    public bool IsAggressive { get; set; }
    public string Level { get; set; } = "";
    public string Hp { get; set; } = "";
    public string Race { get; set; } = "";
    public string Element { get; set; } = "";
    public string Size { get; set; } = "";
    public string Attack { get; set; } = "";
    public string Defense { get; set; } = "";
    public string MagicDefense { get; set; } = "";
    public string BaseExp { get; set; } = "";
    public string JobExp { get; set; } = "";
    public Dictionary<string, string> Stats { get; set; } = [];
    public Dictionary<string, string> Behavior { get; set; } = [];
    public Dictionary<string, string> ElementDamage { get; set; } = [];
    public List<DatabaseMonsterSkill> Skills { get; set; } = [];
    public string ImageUrl { get; set; } = "";
    public List<DatabaseDrop> Drops { get; set; } = [];
    public List<DatabaseSpawn> Spawns { get; set; } = [];
    public string Caption => $"{Name}  ·  Lv {Level}  ·  #{Id}";
}

public sealed class DatabaseMonsterSkill
{
    public string Description { get; set; } = "";
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public string Type { get; set; } = "";
}

public sealed class DatabaseDrop
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Rate { get; set; } = "";
    public bool IsMvp { get; set; }
}

public sealed class DatabaseSpawn
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Count { get; set; } = "";
    public string Respawn { get; set; } = "";
}

public sealed class DatabaseItem
{
    public string AegisName { get; set; } = "";
    public string Description { get; set; } = "";
    public long NpcSellPrice { get; set; }
    public Dictionary<string, string> Stats { get; set; } = [];
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public List<DatabaseItemSource> DroppedBy { get; set; } = [];
    public string Caption => $"{Name}  ·  {Type}  ·  #{Id}";
}

public sealed class DatabaseItemSource
{
    public int MonsterId { get; set; }
    public string Name { get; set; } = "";
    public string Rate { get; set; } = "";
    public bool IsMvp { get; set; }
}

public sealed class DatabaseMap
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<DatabaseMapMonster> Monsters { get; set; } = [];
    public string Caption => $"{Name}  ·  {Code}  ·  {Monsters.Count} mobs";
}

public sealed class DatabaseMapMonster
{
    public int MonsterId { get; set; }
    public string Name { get; set; } = "";
    public string Count { get; set; } = "";
    public string Respawn { get; set; } = "";
}

public enum DatabaseSearchKind { Monsters, Items, Maps, Market }

public sealed class MarketListing
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string Refine { get; set; } = "";
    public string Cards { get; set; } = "";
    public string Price { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Stall { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Map { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public string ShopType { get; set; } = "SELL";
    public string NavigationCommand => $"/navi {Map} {X}/{Y}";
}

public sealed record DatabaseSearchResult(DatabaseSearchKind Kind, object Value, string Caption, string Meta);

public sealed class MonsterSearchFilter
{
    public string Race { get; set; } = "";
    public string Element { get; set; } = "";
    public string Size { get; set; } = "";
    public int LevelMin { get; set; }
    public int LevelMax { get; set; }
    public int BaseExpMin { get; set; }
    public int JobExpMin { get; set; }
    public int HpMin { get; set; }
    public int HpMax { get; set; }
    public int ElementLevelMin { get; set; }
    public bool AggressiveOnly { get; set; }
    public bool BossOnly { get; set; }
    public bool MvpOnly { get; set; }
}

public sealed class SpringRoItemDetails
{
    public string ImageUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public string NpcSellValue { get; set; } = "";
    public string ServerCopies { get; set; } = "";
}

public readonly record struct MapExperienceDensity(int MonsterCount, long BaseExperience, long JobExperience);
