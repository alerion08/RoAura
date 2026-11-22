using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    private void ShowCrafting(DatabaseItem item)
    {
        var recipes = RagnarokPlusRecipes.ForItem(item.Id).ToArray();
        var uses = RagnarokPlusRecipes.UsingItem(item.Id).ToArray();
        if (recipes.Length > 0)
        {
            AddSection(T("Рецепты", "Recipes"));
            var common = recipes[0].Materials.Select(part => (part.ItemId, part.Amount, part.IsGuide)).ToHashSet();
            foreach (var recipe in recipes.Skip(1))
                common.IntersectWith(recipe.Materials.Select(part => (part.ItemId, part.Amount, part.IsGuide)));
            if (recipes.Length > 1)
                DetailsPanel.Children.Add(Meta(T("★ — различие между вариантами", "★ marks a difference between recipes")));
            var tabs = new TabControl { Margin = new Thickness(0, 2, 0, 4) };
            for (var index = 0; index < recipes.Length; index++)
            {
                var recipe = recipes[index];
                var ingredients = new StackPanel { Margin = new Thickness(6) };
                ingredients.Children.Add(Meta(T($"Выход за крафт: {recipe.Amount}", $"Output per craft: {recipe.Amount}")));
                var target = new TextBox { Text = Math.Max(1, recipe.Amount).ToString(), Width = 70,
                    HorizontalAlignment = HorizontalAlignment.Left };
                ingredients.Children.Add(Meta(T("Сколько изделий нужно", "Desired output")));
                ingredients.Children.Add(target);
                foreach (var material in recipe.Materials)
                {
                    var usage = material.IsGuide ? T("руководство", "guide") : $"×{material.Amount}";
                    var difference = recipes.Length > 1 && !common.Contains((material.ItemId, material.Amount, material.IsGuide))
                        ? "★ " : "";
                    var button = new Button
                    {
                        Content = new TextBlock { Text = $"{difference}{material.Name} · {usage}", TextWrapping = TextWrapping.Wrap },
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 2, 0, 2),
                        IsEnabled = _database.Item(material.ItemId) is not null
                    };
                    button.Click += (_, _) => OpenItem(material.ItemId);
                    ingredients.Children.Add(button);
                    if (material.IsGuide) continue;

                    var stock = new TextBox
                    {
                        Text = _settings.RagnarokPlusCraftStock.GetValueOrDefault(material.ItemId).ToString(),
                        Width = 70, HorizontalAlignment = HorizontalAlignment.Left
                    };
                    var remaining = Meta("");
                    void UpdateAmount()
                    {
                        if (!int.TryParse(target.Text, out var count) || count <= 0 ||
                            !int.TryParse(stock.Text, out var owned) || owned < 0)
                        {
                            remaining.Text = T("Укажи целые количества", "Enter whole quantities");
                            return;
                        }
                        var batch = Math.Max(1, recipe.Amount);
                        var required = ((count + (long)batch - 1) / batch) * material.Amount;
                        var missing = Math.Max(0, required - owned);
                        remaining.Text = T($"Нужно {required} · Не хватает {missing}", $"Need {required} · Missing {missing}");
                    }

                    target.TextChanged += (_, _) => UpdateAmount();
                    stock.TextChanged += (_, _) => UpdateAmount();
                    stock.GotKeyboardFocus += (_, _) =>
                        stock.Text = _settings.RagnarokPlusCraftStock.GetValueOrDefault(material.ItemId).ToString();
                    stock.LostKeyboardFocus += (_, _) =>
                    {
                        if (!int.TryParse(stock.Text, out var owned) || owned < 0) return;
                        _settings.RagnarokPlusCraftStock[material.ItemId] = owned;
                        (Application.Current as App)?.SaveDatabasePreferences();
                    };
                    ingredients.Children.Add(Meta(T("В наличии", "In stock")));
                    ingredients.Children.Add(stock);
                    ingredients.Children.Add(remaining);
                    UpdateAmount();
                }
                tabs.Items.Add(new TabItem { Header = (index + 1).ToString(), Content = ingredients });
            }
            DetailsPanel.Children.Add(tabs);
        }

        if (uses.Length == 0) return;
        var products = new StackPanel { Margin = new Thickness(6, 3, 0, 0) };
        var view = _itemCardView;
        var expander = new Expander
        {
            Header = T($"Что можно изготовить ({uses.Length})", $"Recipes using this item ({uses.Length})"),
            Content = products,
            IsExpanded = view?.RecipesExpanded ?? false,
            Margin = new Thickness(0, 6, 0, 4)
        };
        if (view is not null)
        {
            expander.Expanded += (_, _) => view.RecipesExpanded = true;
            expander.Collapsed += (_, _) => view.RecipesExpanded = false;
        }
        DetailsPanel.Children.Add(expander);
        foreach (var recipe in uses.DistinctBy(recipe => recipe.ItemId).OrderBy(recipe => recipe.Name))
        {
            if (_database.Item(recipe.ItemId) is not null)
                AddLink(recipe.Name, () => OpenItem(recipe.ItemId), products);
            else
                products.Children.Add(Meta(recipe.Name));
        }
    }
}
