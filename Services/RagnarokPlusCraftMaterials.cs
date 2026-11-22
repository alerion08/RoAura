using System.Text.Json;

namespace RoAura.Services;

internal static class RagnarokPlusCraftMaterials
{
    private static readonly Lazy<HashSet<int>> Materials = new(Load);

    internal static bool Contains(int id) => id > 0 && Materials.Value.Contains(id);

    private static HashSet<int> Load()
    {
        try
        {
            using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "ragnarokplus-materials.json"));
            return JsonSerializer.Deserialize<HashSet<int>>(file) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DebugLog.Write($"RAGNAROK+ CRAFT DATA: {ex.Message}");
            return [];
        }
    }
}
