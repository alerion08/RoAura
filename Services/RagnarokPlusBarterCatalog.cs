using System.IO.Compression;
using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

internal static class RagnarokPlusBarterCatalog
{
    internal sealed record Cost(int? ItemId, string Name, long? Quantity, string Kind, string? Icon);
    internal sealed record Location(string Code, string[] Pins, string? Image);
    internal sealed record Offer(int ShopId, int Ordinal, string Name, int? ItemId, long? Quantity,
        string Npc, Location[] Maps, string? NpcIcon, string? Icon, string Description, string Details, Cost[] Costs);

    private static string CatalogPath => Path.Combine(AppContext.BaseDirectory, "Data", "ragnarokplus-barter.zip");
    private static readonly Lazy<Offer[]> Catalog = new(Load);
    private static readonly Lazy<HashSet<int>> Materials = new(() => Catalog.Value.SelectMany(offer => offer.Costs)
        .Where(cost => cost.Kind == "item" && cost.ItemId > 0).Select(cost => cost.ItemId!.Value).ToHashSet());

    internal static bool IsMaterial(int id) => id > 0 && Materials.Value.Contains(id);

    internal static IEnumerable<Offer> ForItem(int id) => Catalog.Value.Where(offer => offer.ItemId == id);

    internal static IEnumerable<DatabaseSearchResult> Search(string query)
    {
        query = query.Trim();
        return Catalog.Value.Where(offer => offer.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                offer.Npc.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                offer.ItemId?.ToString() == query.TrimStart('#'))
            .Select(offer => new DatabaseSearchResult(DatabaseSearchKind.Barter, offer, offer.Name,
                $"{offer.Npc}{(offer.ItemId is int id ? $" · #{id}" : "")}"));
    }

    internal static byte[]? ReadImage(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        try
        {
            using var archive = ZipFile.OpenRead(CatalogPath);
            using var stream = archive.GetEntry(key)?.Open();
            if (stream is null) return null;
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            DebugLog.Write($"RAGNAROK+ BARTER IMAGE: {ex.Message}");
            return null;
        }
    }

    private static Offer[] Load()
    {
        try
        {
            using var archive = ZipFile.OpenRead(CatalogPath);
            using var index = archive.GetEntry("offers.json")?.Open();
            if (index is null) return [];
            return (JsonSerializer.Deserialize<Offer[]>(index, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [])
                .OrderBy(offer => offer.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(offer => offer.ShopId).ThenBy(offer => offer.Ordinal).ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            DebugLog.Write($"RAGNAROK+ BARTER DATA: {ex.Message}");
            return [];
        }
    }
}
