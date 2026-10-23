using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

public static class ItemCatalogService
{
    public static IReadOnlyDictionary<int, ItemEntry> Load(string? gameVersion = null)
    {
        var version = GameDatabaseVersion.Normalize(gameVersion);
        var fileName = version == GameDatabaseVersion.SpringRo ? "items.springro.json" : "items.rtm.json";
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Data", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName)
        };
        if (version == GameDatabaseVersion.Rtm)
        {
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "Data", "items.json"));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "items.json"));
        }

        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var items = JsonSerializer.Deserialize<List<ItemEntry>>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                return items.GroupBy(item => item.Id).ToDictionary(group => group.Key,
                    group => group.First());
            }
            catch (Exception ex)
            {
                DebugLog.Write($"ITEM CATALOG ERROR {path}: {ex.Message}");
            }
        }

        return new Dictionary<int, ItemEntry>();
    }
}
