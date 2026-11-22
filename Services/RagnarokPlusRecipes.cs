using System.Text.Json;

namespace RoAura.Services;

internal static class RagnarokPlusRecipes
{
    internal sealed record Ingredient(int ItemId, string Name, int Amount, bool IsGuide);
    internal sealed record Recipe(int Id, int ItemId, string Name, int Amount, Ingredient[] Materials);
    private static readonly Lazy<Recipe[]> Recipes = new(Load);
    private static readonly Lazy<ILookup<int, Recipe>> ByProduct = new(() => Recipes.Value.ToLookup(recipe => recipe.ItemId));
    private static readonly Lazy<ILookup<int, Recipe>> ByMaterial = new(() => Recipes.Value
        .SelectMany(recipe => recipe.Materials.Where(part => !part.IsGuide).Select(part => part.ItemId)
            .Distinct().Select(id => (id, recipe))).ToLookup(entry => entry.id, entry => entry.recipe));

    internal static IEnumerable<Recipe> ForItem(int id) => ByProduct.Value[id];
    internal static IEnumerable<Recipe> UsingItem(int id) => ByMaterial.Value[id];

    private static Recipe[] Load()
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "ragnarokplus-recipes.json"));
            return JsonSerializer.Deserialize<Recipe[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DebugLog.Write($"RAGNAROK+ RECIPES: {ex.Message}");
            return [];
        }
    }
}
