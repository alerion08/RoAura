using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

public static class MonsterCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    // App and MainWindow each load the catalog at startup; parsing the same file
    // again is wasted work, so the parsed result is reused until the file changes.
    private static readonly object CacheSync = new();
    private static readonly Dictionary<string, (DateTime LastWriteUtc, DateTime EnrichmentWriteUtc, IReadOnlyList<MonsterEntry> Monsters)> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<IReadOnlyList<MonsterEntry>> TocMonsters = new(() =>
        TocDatabaseService.Database.Monsters.Select(monster => FromDatabase(monster, GameDatabaseVersion.Toc))
            .OrderBy(monster => monster.Name).ToArray());

    public static IReadOnlyList<MonsterEntry> Load(string? gameVersion = null) =>
        LoadForTimers(gameVersion).Where(monster => monster.IsMvp).ToArray();

    public static IReadOnlyList<MonsterEntry> LoadForTimers(string? gameVersion)
    {
        var version = GameDatabaseVersion.Normalize(gameVersion);
        if (version == GameDatabaseVersion.Toc)
        {
            try { return TocMonsters.Value; }
            catch (Exception ex)
            {
                DebugLog.Write($"TOC TIMER CATALOG ERROR: {ex}");
                return [];
            }
        }

        var monsters = LoadJsonCatalog(version);
        if (version != GameDatabaseVersion.Rtm) return monsters;

        // The bundled RTM catalog has only MVPs. Supply a named reference
        // catalog for ordinary monsters when using RTM or a generic profile.
        var existingIds = monsters.Select(monster => monster.Id).ToHashSet();
        return monsters.Concat(LoadJsonCatalog(GameDatabaseVersion.SpringRo)
            .Where(monster => !monster.IsMvp && !existingIds.Contains(monster.Id))
            .Select(monster => new MonsterEntry
            {
                Id = monster.Id, Name = monster.Name, NameRu = monster.NameRu, IsMvp = false,
                IsReference = true, Spawns = monster.Spawns, LocalImagePath = monster.LocalImagePath
            })).OrderBy(monster => monster.Name).ToArray();
    }

    private static IReadOnlyList<MonsterEntry> LoadJsonCatalog(string version)
    {
        var fileName = version == GameDatabaseVersion.SpringRo ? "monsters.springro.json" : "monsters.rtm.json";
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Data", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName)
        };
        if (version == GameDatabaseVersion.Rtm)
        {
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "Data", "monsters.json"));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "monsters.json"));
        }
        var enrichmentPath = Path.Combine(AppContext.BaseDirectory, "Data", "database.springro.json");
        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var lastWriteUtc = File.GetLastWriteTimeUtc(path);
                var enrichmentWriteUtc = version == GameDatabaseVersion.SpringRo && File.Exists(enrichmentPath)
                    ? File.GetLastWriteTimeUtc(enrichmentPath)
                    : DateTime.MinValue;
                var cacheKey = $"{version}|{path}";
                lock (CacheSync)
                {
                    if (Cache.TryGetValue(cacheKey, out var cached) &&
                        cached.LastWriteUtc == lastWriteUtc && cached.EnrichmentWriteUtc == enrichmentWriteUtc)
                        return cached.Monsters;
                }

                var result = JsonSerializer.Deserialize<List<MonsterEntry>>(File.ReadAllText(path), JsonOptions) ?? [];
                if (version == GameDatabaseVersion.SpringRo)
                    EnrichSpringMonsters(result, enrichmentPath);
                foreach (var monster in result)
                    monster.LocalImagePath = LocalSprite(version, monster.Id);
                var ordered = result.OrderBy(monster => monster.Name).ToList();
                lock (CacheSync) Cache[cacheKey] = (lastWriteUtc, enrichmentWriteUtc, ordered);
                return ordered;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"MONSTER CATALOG ERROR {path}: {ex}");
            }
        }
        return [];
    }

    private static void EnrichSpringMonsters(List<MonsterEntry> monsters, string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            var database = JsonSerializer.Deserialize<GameDatabase>(File.ReadAllText(path), JsonOptions);
            if (database is null) return;

            var detailsById = database.Monsters.GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var monster in monsters)
            {
                if (!detailsById.TryGetValue(monster.Id, out var details)) continue;
                monster.Spawns = ReadSpawns(details);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SPRING TIMER DETAILS ERROR {path}: {ex}");
        }
    }

    private static MonsterEntry FromDatabase(DatabaseMonster monster, string version) => new()
    {
        Id = monster.Id, Name = monster.Name, IsMvp = monster.IsMvp,
        LocalImagePath = LocalSprite(version, monster.Id), Spawns = ReadSpawns(monster)
    };

    private static List<MonsterSpawnEntry> ReadSpawns(DatabaseMonster monster) =>
        monster.Spawns.Select(spawn => new MonsterSpawnEntry
        {
            Code = spawn.Code, Name = spawn.Name, Count = spawn.Count, Respawn = spawn.Respawn
        }).ToList();

    private static string LocalSprite(string version, int id)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Data", "TimerMonsters", version);
        foreach (var extension in new[] { ".png", ".gif" })
        {
            var path = Path.Combine(directory, id + extension);
            if (File.Exists(path)) return path;
        }
        return "";
    }
}
