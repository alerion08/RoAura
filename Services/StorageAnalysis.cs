using System.Text.Json;

namespace RoAura.Services;

internal static class StorageAnalysis
{
    internal static bool HasCostumePrefix(string? name)
    {
        var trimmed = name?.TrimStart();
        return trimmed?.StartsWith("C ", StringComparison.OrdinalIgnoreCase) == true ||
               trimmed?.StartsWith("Costume ", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal sealed record Entry(int ItemId, long Amount, string Mode, int Slot, string? Name = null, string? Type = null);
    internal sealed record Material(int ItemId, string Name, long Required, long Available)
    {
        public long Missing => Math.Max(0, Required - Available);
    }
    internal sealed record Craft(string Name, object Target, Material[] Materials, long? Output = null, int? ResultId = null, int? RecipeId = null)
    {
        public long Repeats => Materials.Min(material => material.Available / material.Required);
        public int Percent => (int)Math.Floor(Materials.Average(material =>
            Math.Min(1m, (decimal)material.Available / material.Required)) * 100m);
    }

    internal static Entry[] Parse(JsonElement data, JsonElement metadata = default, Action<string, int, string>? progress = null)
    {
        if (data.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid storage response.");
        var entries = new List<Entry>();
        var recognized = false;
        foreach (var mode in new[] { "sc", "hc" })
        for (var slot = 1; slot <= 3; slot++)
        {
            var key = slot == 1 ? mode : mode + slot;
            if (!data.TryGetProperty(key, out var rows)) continue;
            recognized = true;
            if (rows.ValueKind == JsonValueKind.Null) continue;
            if (rows.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid storage group.");
            var types = new HashSet<string>();
            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("nameid", out var rawId) || !int.TryParse(rawId.ToString(), out var id) || id <= 0 ||
                    !row.TryGetProperty("amount", out var rawAmount) || !long.TryParse(rawAmount.ToString(), out var amount) || amount <= 0)
                    throw new InvalidDataException("Invalid storage item.");
                var meta = metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty(id.ToString(), out var value)
                    ? value : default;
                var name = ReadText(meta, "name");
                var type = ReadText(meta, "type");
                if (meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("locations", out var locations) &&
                    ((locations.ValueKind == JsonValueKind.Array && locations.EnumerateArray().Any(location =>
                        location.ValueKind == JsonValueKind.String && location.GetString()!.StartsWith("Costume_", StringComparison.Ordinal))) ||
                     (locations.ValueKind == JsonValueKind.Object && locations.EnumerateObject().Any(location =>
                        location.Name.StartsWith("Costume_", StringComparison.Ordinal) &&
                        (location.Value.ValueKind == JsonValueKind.True || location.Value.ToString() == "1")))))
                    type = "Costume";
                entries.Add(new Entry(id, amount, mode.ToUpperInvariant(), slot, name, type));
                if (types.Add(type ?? "—")) progress?.Invoke(mode.ToUpperInvariant(), slot, type ?? "—");
            }
        }
        if (!recognized) throw new InvalidDataException("No storage groups in the response.");
        return entries.ToArray();
    }

    private static string? ReadText(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.String
            ? child.GetString() : null;

    internal static Craft[] Plans(IReadOnlyDictionary<int, long> stock,
        Func<RagnarokPlusBarterCatalog.Cost, int?> resolveCost, int threshold = 60)
    {
        var options = RagnarokPlusRecipes.All.Select(r => (r.Name, Target: (object)r.ItemId, Output: (long?)r.Amount, ResultId: (int?)r.ItemId, RecipeId: (int?)r.Id, Parts:
                r.Materials.Where(p => !p.IsGuide).Select(p => (Id: (int?)p.ItemId, p.Name, Count: (long?)p.Amount))))
            .Concat(RagnarokPlusBarterCatalog.All.Select(o => (o.Name, Target: (object)o, Output: o.Quantity, ResultId: o.ItemId, RecipeId: (int?)null, Parts:
                o.Costs.Where(p => p.Kind == "item").Select(p => (Id: resolveCost(p), p.Name, Count: p.Quantity)))));
        return options.Select(option =>
        {
            var parts = option.Parts.ToArray();
            if (parts.Length == 0 || parts.Any(p => p.Id is not > 0 || p.Count is not > 0)) return null;
            var materials = parts.GroupBy(p => p.Id!.Value).Select(group =>
                new Material(group.Key, group.First().Name, group.Sum(p => p.Count!.Value), stock.GetValueOrDefault(group.Key))).ToArray();
            return new Craft(option.Name, option.Target, materials, option.Output, option.ResultId, option.RecipeId);
        }).OfType<Craft>().Where(c => c.Percent >= Math.Clamp(threshold, 0, 100))
          .OrderByDescending(c => c.Percent).ThenBy(c => c.Name).ToArray();
    }
}
