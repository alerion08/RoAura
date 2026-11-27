using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    private void ShowBarter(RagnarokPlusBarterCatalog.Offer offer)
    {
        _appearanceRedraw = () => { DetailsPanel.Children.Clear(); ShowBarter(offer); };
        var reward = FindBarterItem(offer.ItemId, offer.Name);
        AddBarterLine(offer.Name + (offer.Quantity > 1 ? $" ×{offer.Quantity:N0}" : ""), offer.Icon, 20,
            offer.ItemId ?? reward?.Id);
        if ((offer.ItemId ?? reward?.Id) is int id) DetailsPanel.Children.Add(Meta($"Item ID #{id}"));
        var description = string.IsNullOrWhiteSpace(offer.Description) ? reward?.Description : offer.Description;
        if (!string.IsNullOrWhiteSpace(description)) DetailsPanel.Children.Add(Meta(description));

        if (!string.IsNullOrWhiteSpace(offer.Details)) DetailsPanel.Children.Add(Meta(offer.Details));

        AddBarterLine(offer.Npc, offer.NpcIcon, 16);
        foreach (var location in offer.Maps)
        {
            DetailsPanel.Children.Add(Meta(string.Join("\n", location.Pins.Prepend(location.Code))));
            if (RagnarokPlusBarterCatalog.ReadImage(location.Image) is { } mapImage)
                DetailsPanel.Children.Add(new Image { Source = DecodeCatalogImage(mapImage), MaxWidth = 320, MaxHeight = 320,
                    Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
        }

        DetailsPanel.Children.Add(Heading(T("Стоимость обмена", "Exchange cost"), 16));
        foreach (var cost in offer.Costs)
        {
            var item = cost.Kind == "item" ? FindBarterItem(cost.ItemId, cost.Name) : null;
            var label = cost.Kind == "zeny" ? $"{cost.Quantity:N0} Zeny" :
                $"{cost.Name} ×{(cost.Quantity?.ToString("N0") ?? "?")}";
            AddBarterLine(label, cost.Icon, 0, cost.ItemId ?? item?.Id);
        }

        var materials = offer.Costs.Where(cost => cost.Kind == "item").Select(cost =>
            (Id: cost.ItemId ?? FindBarterItem(null, cost.Name)?.Id ?? 0, cost.Name, Target: cost.Quantity ?? 0)).ToArray();
        var canImport = materials.Length > 0 && materials.All(cost => cost.Id > 0 && cost.Target > 0);
        var add = new Button { Content = T("Добавить в список добычи", "Add to loot list"), IsEnabled = canImport };
        var notice = Meta(canImport ? "" : T("Для списка добычи нужны ID и количество каждого материала.", "Loot lists require an ID and quantity for every material."));
        add.Click += (_, _) =>
        {
            var targets = materials.GroupBy(cost => cost.Id).Select(group =>
                (group.Key, group.First().Name, group.Sum(cost => cost.Target)));
            LootLists.ImportQuest(_settings, GameServerProfile.RagnarokPlus, $"barter:{offer.ShopId}:{offer.Ordinal}",
                offer.Name, targets, reuseUnlinkedList: false);
            (Application.Current as App)?.LootListsChanged();
            notice.Text = T("Материалы добавлены в список добычи.", "Materials added to the loot list.");
        };
        DetailsPanel.Children.Add(add);
        DetailsPanel.Children.Add(notice);
    }

    private DatabaseItem? FindBarterItem(int? id, string name)
    {
        if (id is int known) return _database.Item(known);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var matches = _database.Search(DatabaseSearchKind.Items, name).Select(result => result.Value)
            .OfType<DatabaseItem>().Where(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private void AddBarterLine(string label, string? icon, double headingSize, int? itemId = null)
    {
        var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        if (RagnarokPlusBarterCatalog.ReadImage(icon) is { } image)
            line.Children.Add(new Image { Source = DecodeCatalogImage(image), Width = 32, Height = 32,
                Margin = new Thickness(0, 0, 6, 0), Stretch = Stretch.Uniform });
        if (itemId is int id && _database.Item(id) is not null)
        {
            var link = new Button { Content = headingSize > 0 ? Heading(label, headingSize) : Meta(label),
                HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += (_, _) => OpenItem(id);
            line.Children.Add(link);
        }
        else line.Children.Add(headingSize > 0 ? Heading(label, headingSize) : Meta(label));
        DetailsPanel.Children.Add(line);
    }
}
