using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

public static class MonsterCatalogService
{
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
        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var result = JsonSerializer.Deserialize<List<MonsterEntry>>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                if (version == GameDatabaseVersion.SpringRo)
                    EnrichSpringMonsters(result);
                return result.Where(item => item.IsMvp).OrderBy(item => item.Name).ToList();
            }
            catch (Exception ex)
            {
                DebugLog.Write($"MONSTER CATALOG ERROR {path}: {ex.Message}");
            }
        }
        return [];
    }

    private static void EnrichSpringMonsters(List<MonsterEntry> monsters)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "database.springro.json");
        if (!File.Exists(path)) return;

        try
        {
            var database = JsonSerializer.Deserialize<GameDatabase>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (database is null) return;

            var detailsById = database.Monsters.ToDictionary(item => item.Id);
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
            DebugLog.Write($"SPRING MVP DETAILS ERROR {path}: {ex.Message}");
        }
    }
}
