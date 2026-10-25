using System.Text.Json;
using System.Text.RegularExpressions;
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
                var result = items.GroupBy(item => item.Id).ToDictionary(group => group.Key,
                    group => group.First());
                if (version == GameDatabaseVersion.SpringRo)
                {
                    ApplySpringRoExactNames(result);
                    result[7539] = new ItemEntry { Id = 7539, Name = "Poring Coin", Type = "Loot" };
                }
                return result;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"ITEM CATALOG ERROR {path}: {ex.Message}");
            }
        }

        return new Dictionary<int, ItemEntry>();
    }

    private static void ApplySpringRoExactNames(Dictionary<int, ItemEntry> items)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "database.springro.json");
            if (!File.Exists(path)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("Monsters", out var monsters)) return;
            foreach (var monster in monsters.EnumerateArray())
            {
                if (!monster.TryGetProperty("Drops", out var drops)) continue;
                foreach (var drop in drops.EnumerateArray())
                {
                    if (!drop.TryGetProperty("ItemId", out var idValue) ||
                        !drop.TryGetProperty("Name", out var nameValue) ||
                        !items.TryGetValue(idValue.GetInt32(), out var item)) continue;
                    var exactName = nameValue.GetString()?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(exactName)) item.ExactName = exactName;
                }
            }

            foreach (var group in items.Values.Where(item => item.Type == "Equip")
                         .GroupBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                var variants = group.OrderBy(item => item.Id).ToList();
                var anchorIndex = variants.FindIndex(item => SlotSuffix(item.ExactName) >= 0);
                if (anchorIndex >= 0)
                {
                    var anchorSlots = SlotSuffix(variants[anchorIndex].ExactName);
                    for (var index = 0; index < variants.Count; index++)
                    {
                        if (!string.IsNullOrWhiteSpace(variants[index].ExactName) &&
                            SlotSuffix(variants[index].ExactName) >= 0) continue;
                        var slots = Math.Max(0, anchorSlots + index - anchorIndex);
                        variants[index].ExactName = slots > 0 ? $"{variants[index].Name} [{slots}]" : variants[index].Name;
                    }
                }

                var duplicateNames = variants.GroupBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase)
                    .Where(duplicate => duplicate.Count() > 1);
                foreach (var duplicate in duplicateNames)
                    foreach (var item in duplicate)
                        item.ExactName = $"{item.FullName} · Item ID {item.Id}";
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SPRING ITEM NAME ENRICHMENT ERROR: {ex.Message}");
        }
    }

    private static int SlotSuffix(string value)
    {
        var match = Regex.Match(value ?? "", @"\[(\d+)\]\s*$");
        return match.Success && int.TryParse(match.Groups[1].Value, out var slots) ? slots : -1;
    }
}
