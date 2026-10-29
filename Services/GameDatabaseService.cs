using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

public sealed class GameDatabaseService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private GameDatabase _database = new();
    private IReadOnlyDictionary<int, DatabaseMonster> _monstersById = new Dictionary<int, DatabaseMonster>();
    public IReadOnlyList<DatabaseMonster> Monsters => _database.Monsters;
    public IReadOnlyList<DatabaseItem> Items => _database.Items;
    public IReadOnlyList<DatabaseMap> Maps => _database.Maps;

    public string Version { get; private set; } = GameDatabaseVersion.SpringRo;
    public GameDatabaseService(string? version = GameDatabaseVersion.SpringRo) => Reload(version);
    public void Reload(string? version)
    {
        Version = GameDatabaseVersion.Normalize(version);
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "database.springro.json");
        try
        {
            _database = Version == GameDatabaseVersion.Toc ? TocDatabaseService.Database :
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

        // Generated JSON may repeat an ID; a plain ToDictionary would throw here and
        // take the whole application down at startup. First occurrence wins.
        var groups = _database.Monsters.GroupBy(x => x.Id).ToList();
        var duplicates = groups.Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicates.Count > 0)
            DebugLog.Write($"DATABASE: {duplicates.Count} duplicate monster IDs ignored (first wins): {string.Join(", ", duplicates.Take(20))}");
        _monstersById = groups.ToDictionary(group => group.Key, group => group.First());
    }

    public IEnumerable<DatabaseSearchResult> Search(DatabaseSearchKind kind, string query, int limit = 100)
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
            DatabaseSearchKind.Items => _database.Items.Where(x => Matches(x.Name, x.Id.ToString()))
                .OrderByDescending(x => x.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)).ThenBy(x => x.Name)
                .Take(limit).Select(x => new DatabaseSearchResult(kind, x, x.Name, $"{x.Type} · #{x.Id}")),
            _ => _database.Maps.Where(x => Matches(x.Name, x.Code))
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

    public IEnumerable<DatabaseSearchResult> SearchMonsters(string query, MonsterSearchFilter filter, int limit = 100)
    {
        query = query.Trim();
        bool Matches(string name, string id) => query.Length == 0 ||
            name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            id.Contains(query, StringComparison.OrdinalIgnoreCase);
        bool TextMatches(string actual, string wanted) => string.IsNullOrWhiteSpace(wanted) ||
            actual.StartsWith(wanted, StringComparison.OrdinalIgnoreCase);

        return _database.Monsters
            .Where(x => Matches(x.Name, x.Id.ToString()))
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
    public DatabaseItem? Item(int id) => _database.Items.FirstOrDefault(x => x.Id == id);
    public DatabaseMap? Map(string code) => _database.Maps.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}
