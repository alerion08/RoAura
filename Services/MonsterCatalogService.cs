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

    public static IReadOnlyList<MonsterEntry> Load(string? gameVersion = null)
    {
        var version = GameDatabaseVersion.Normalize(gameVersion);
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
                var mvps = result.Where(item => item.IsMvp).OrderBy(item => item.Name).ToList();
                lock (CacheSync) Cache[cacheKey] = (lastWriteUtc, enrichmentWriteUtc, mvps);
                return mvps;
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
                monster.Spawns = details.Spawns.Select(spawn => new MonsterSpawnEntry
                {
                    Code = spawn.Code,
                    Name = spawn.Name,
                    Count = spawn.Count,
                    Respawn = spawn.Respawn
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SPRING MVP DETAILS ERROR {path}: {ex}");
        }
    }
}
