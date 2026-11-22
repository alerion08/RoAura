using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

public sealed class GameDatabaseService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private GameDatabase _database = new();
    private IReadOnlyDictionary<int, DatabaseMonster> _monstersById = new Dictionary<int, DatabaseMonster>();
    private IReadOnlyDictionary<int, DatabaseItem> _itemsById = new Dictionary<int, DatabaseItem>();
    private IReadOnlyDictionary<int, SpringRoItemPrice> _npcPrices = new Dictionary<int, SpringRoItemPrice>();
    public IReadOnlyList<DatabaseMonster> Monsters => _database.Monsters;
    public IReadOnlyList<DatabaseItem> Items => _database.Items;
    public IReadOnlyList<DatabaseMap> Maps => _database.Maps;

    public string Version { get; private set; } = GameDatabaseVersion.SpringRo;
    public GameDatabaseService(string? version = GameDatabaseVersion.SpringRo) => Reload(version);
    public void Reload(string? version)
    {
        Version = GameDatabaseVersion.Normalize(version);
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "database.springro.json");
        if (Version == GameDatabaseVersion.SpringRo && ServerCatalogDownload.IsInstalled(Version))
            path = ServerCatalogDownload.InstalledPath("springro");
        try
        {
            _database = Version == GameServerProfile.RagnarokPlus ? RagnarokPlusData.LoadCatalog() :
                Version == GameDatabaseVersion.Toc ? TocDatabaseService.Database :
                JsonSerializer.Deserialize<GameDatabase>(File.ReadAllText(path), JsonOptions) ?? new GameDatabase();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"DATABASE LOAD ERROR: {ex}");
            _database = new GameDatabase { Source = Version };
        }
        // The item catalog carries the server's full equipment names, including slot suffixes.
        var catalog = ItemCatalogService.Load(Version);
        foreach (var item in _database.Items)
            if (catalog.TryGetValue(item.Id, out var entry) && !string.IsNullOrWhiteSpace(entry.FullName))
                item.Name = entry.FullName;

        _itemsById = _database.Items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        _npcPrices = Version == GameDatabaseVersion.SpringRo ? GoldGoblinService.LoadPrices(Version) :
            new Dictionary<int, SpringRoItemPrice>();

        // Generated JSON may repeat an ID; a plain ToDictionary would throw here and
        // take the whole application down at startup. First occurrence wins.
        var groups = _database.Monsters.GroupBy(x => x.Id).ToList();
        var duplicates = groups.Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicates.Count > 0)
            DebugLog.Write($"DATABASE: {duplicates.Count} duplicate monster IDs ignored (first wins): {string.Join(", ", duplicates.Take(20))}");
        _monstersById = groups.ToDictionary(group => group.Key, group => group.First());
    }

    public IEnumerable<DatabaseSearchResult> Search(DatabaseSearchKind kind, string query, int limit = 100, bool includeItemEffects = false,
        int mapMonsterLevelMin = 0, int mapMonsterLevelMax = 0, int itemSlotsMin = 0, bool sortItemsByNpcPrice = false)
    {
        query = query.Trim();
        bool Matches(string name, string id) => query.Length == 0 ||
            name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            id.Contains(query, StringComparison.OrdinalIgnoreCase);

        return kind switch
        {
            DatabaseSearchKind.Monsters => _database.Monsters.Where(x => Matches(x.Name, x.Id.ToString()))
                .OrderByDescending(x => x.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)).ThenBy(x => x.Name)
                .Take(limit).Select(x => new DatabaseSearchResult(kind, x, x.Name, $"Lv {x.Level} · {x.Element} · #{x.Id}")),
            DatabaseSearchKind.Items => SearchItems(query, limit, includeItemEffects, itemSlotsMin, sortItemsByNpcPrice),
            _ => _database.Maps.Where(x => Matches(x.Name, x.Code))
                .Where(x => (mapMonsterLevelMin <= 0 && mapMonsterLevelMax <= 0) ||
                    x.Monsters.Any(spawn => _monstersById.TryGetValue(spawn.MonsterId, out var monster) &&
                        int.TryParse(monster.Level, out var level) && level > 0 &&
                        level >= mapMonsterLevelMin && (mapMonsterLevelMax <= 0 || level <= mapMonsterLevelMax)))
                .OrderByDescending(x => x.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)).ThenBy(x => x.Name)
                .Take(limit).Select(x =>
                {
                    var density = GetMapExperienceDensity(x);
                    return new DatabaseSearchResult(kind, x,
                        $"{x.Name} · Base {density.BaseExperience:N0} / Job {density.JobExperience:N0}",
                        $"{x.Code} · {density.MonsterCount:N0} mobs");
                })
        };
    }

    public long NpcSellPrice(DatabaseItem item) => _npcPrices.GetValueOrDefault(item.Id)?.NpcSellPrice ?? item.NpcSellPrice;

    private IEnumerable<DatabaseSearchResult> SearchItems(string query, int limit, bool includeEffects, int minSlots, bool byPrice)
    {
        var filter = new ItemDatabaseQuery(query);
        var items = _database.Items.Where(item => filter.Matches(item, includeEffects) &&
            (minSlots <= 0 || ItemDatabaseQuery.Slots(item) >= minSlots));
        var sorted = byPrice ? items.OrderByDescending(NpcSellPrice) :
            items.OrderByDescending(item => item.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase));
        return sorted.ThenBy(item => item.Name).Take(limit).Select(item =>
            new DatabaseSearchResult(DatabaseSearchKind.Items, item, item.Name, $"{item.Type} · #{item.Id}" +
                (byPrice ? $" · NPC {NpcSellPrice(item):N0} z" : "")));
    }

    public decimal TypicalNpcDropValue(DatabaseMonster monster)
    {
        return monster.Drops.Where(drop => !drop.IsMvp).Sum(drop =>
            _itemsById.TryGetValue(drop.ItemId, out var item) &&
            decimal.TryParse(drop.Rate.Trim().TrimEnd('%').Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var chance)
                ? NpcSellPrice(item) * chance / 100m : 0m);
    }

    public string? SuggestName(DatabaseSearchKind kind, string query)
    {
        query = query.Trim().ToUpperInvariant();
        if (query.Length is < 4 or > 80 || query.All(char.IsDigit)) return null;
        IEnumerable<string> names = kind switch
        {
            DatabaseSearchKind.Items => _database.Items.Select(item => item.Name),
            DatabaseSearchKind.Monsters => _database.Monsters.Select(monster => monster.Name),
            DatabaseSearchKind.Maps => _database.Maps.Select(map => map.Name),
            _ => []
        };
        var cutoff = query.Length < 7 ? 1 : 2;
        var row = new int[query.Length + 1];
        var previous = new int[row.Length];
        string? best = null;
        foreach (var name in names)
        {
            if (Math.Abs(name.Length - query.Length) > cutoff) continue;
            for (var col = 0; col < row.Length; col++) previous[col] = col;
            for (var index = 0; index < name.Length; index++)
            {
                row[0] = index + 1;
                var letter = char.ToUpperInvariant(name[index]);
                for (var col = 1; col < row.Length; col++)
                    row[col] = Math.Min(Math.Min(row[col - 1] + 1, previous[col] + 1),
                        previous[col - 1] + (query[col - 1] == letter ? 0 : 1));
                (row, previous) = (previous, row);
            }
            if (previous[^1] > cutoff) continue;
            cutoff = previous[^1] - 1;
            best = name;
        }
        return best;
    }

    public IEnumerable<DatabaseSearchResult> SearchMonsters(string query, MonsterSearchFilter filter, int limit = 100)
    {
        query = query.Trim();
        bool Matches(string name, string id) => query.Length == 0 ||
            name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            id.Contains(query, StringComparison.OrdinalIgnoreCase);
        bool TextMatches(string actual, string wanted) => string.IsNullOrWhiteSpace(wanted) ||
            actual.StartsWith(wanted, StringComparison.OrdinalIgnoreCase);

        var dropQuery = filter.DropItem.Trim();
        var dropIds = dropQuery.Length == 0 ? null : int.TryParse(dropQuery.TrimStart('#'), out var id)
            ? new HashSet<int> { id }
            : _database.Items.Where(item => item.Name.Contains(dropQuery, StringComparison.CurrentCultureIgnoreCase))
                .Select(item => item.Id).ToHashSet();

        return _database.Monsters
            .Where(x => Matches(x.Name, x.Id.ToString()))
            .Where(x => dropIds is null || x.Drops.Any(drop => dropIds.Contains(drop.ItemId)))
            .Where(x => TextMatches(x.Race, filter.Race))
            .Where(x => TextMatches(x.Element, filter.Element))
            .Where(x => TextMatches(x.Size, filter.Size))
            .Where(x => filter.LevelMin <= 0 || ParseNumber(x.Level) >= filter.LevelMin)
            .Where(x => filter.LevelMax <= 0 || ParseNumber(x.Level) <= filter.LevelMax)
            .Where(x => filter.BaseExpMin <= 0 || ParseNumber(x.BaseExp) >= filter.BaseExpMin)
            .Where(x => filter.JobExpMin <= 0 || ParseNumber(x.JobExp) >= filter.JobExpMin)
            .Where(x => filter.HpMin <= 0 || ParseNumber(x.Hp) >= filter.HpMin)
            .Where(x => filter.HpMax <= 0 || ParseNumber(x.Hp) <= filter.HpMax)
            .Where(x => filter.ElementLevelMin <= 0 || ParseElementLevel(x.Element) >= filter.ElementLevelMin)
            .Where(x => !filter.AggressiveOnly || x.IsAggressive)
            .Where(x => !filter.BossOnly || x.IsBoss)
            .Where(x => !filter.MvpOnly || x.IsMvp)
            .OrderByDescending(x => x.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            .ThenBy(x => x.Name)
            .Take(limit)
            .Select(x => new DatabaseSearchResult(DatabaseSearchKind.Monsters, x, x.Name,
                $"Lv {x.Level} · {x.Element} · #{x.Id}"));
    }

    public IReadOnlyList<string> MonsterRaces => _database.Monsters.Select(x => x.Race)
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    public IReadOnlyList<string> MonsterElements => _database.Monsters.Select(x => x.Element.Split(' ', 2)[0])
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    public IReadOnlyList<string> MonsterSizes => _database.Monsters.Select(x => x.Size)
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    public MapExperienceDensity GetMapExperienceDensity(DatabaseMap map)
    {
        long totalBase = 0;
        long totalJob = 0;
        var totalMonsters = 0;
        foreach (var spawn in map.Monsters)
        {
            var count = ParseNumber(spawn.Count);
            if (count <= 0 || !_monstersById.TryGetValue(spawn.MonsterId, out var monster)) continue;
            totalMonsters += count;
            totalBase += ParseNumber(monster.BaseExp) * (long)count;
            totalJob += ParseNumber(monster.JobExp) * (long)count;
        }

        if (totalMonsters == 0) return new MapExperienceDensity(0, 0, 0);
        return new MapExperienceDensity(totalMonsters,
            (long)Math.Round(totalBase / (double)totalMonsters),
            (long)Math.Round(totalJob / (double)totalMonsters));
    }

    private static int ParseNumber(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : 0;
    }

    private static int ParseElementLevel(string value)
    {
        var last = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return int.TryParse(last, out var level) ? level : 0;
    }

    public DatabaseMonster? Monster(int id) => _monstersById.GetValueOrDefault(id);
    public DatabaseItem? Item(int id) => _itemsById.GetValueOrDefault(id);
    public DatabaseMap? Map(string code) => _database.Maps.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}
