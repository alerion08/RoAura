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
                return result.Where(item => item.IsMvp).OrderBy(item => item.Name).ToList();
            }
            catch (Exception ex)
            {
                DebugLog.Write($"MONSTER CATALOG ERROR {path}: {ex.Message}");
            }
        }
        return [];
    }
}
