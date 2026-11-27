using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.Text.RegularExpressions;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    private void ShowBarter(RagnarokPlusBarterCatalog.Offer offer)
    {
        var reward = FindBarterItem(offer.ItemId, offer.Name);
        if (reward is null && offer.ItemId is int id)
            reward = new DatabaseItem { Id = id, Name = offer.Name, Description = offer.Description,
                LocalImagePng = RagnarokPlusBarterCatalog.ReadImage(offer.Icon) };
        if (reward is not null)
            ShowItem(reward, new SpringRoItemDetails { Description = reward.Description }, false, offer);
        else
        {
            DetailsPanel.Children.Add(Heading(offer.Name, 20));
            AddBarterSection(offer, DetailsPanel);
        }
    }

    private void AddBarterSection(RagnarokPlusBarterCatalog.Offer offer, Panel panel)
    {
        panel.Children.Add(Heading(T("Бартер", "Barter"), 16));
        if (offer.Quantity > 1) panel.Children.Add(Meta(T($"За обмен: {offer.Quantity:N0}", $"Per exchange: {offer.Quantity:N0}")));
        AddBarterLine(panel, offer.Npc, offer.NpcIcon, 16);
        foreach (var location in offer.Maps)
        {
            panel.Children.Add(Meta(location.Code));
            foreach (var pin in location.Pins)
            {
                panel.Children.Add(Meta(pin));
                var coordinates = Regex.Match(pin, @"(\d+)\s*,\s*(\d+)\s*$");
                if (!coordinates.Success || string.IsNullOrWhiteSpace(location.Code)) continue;
                var command = $"/navi {location.Code} {coordinates.Groups[1].Value}/{coordinates.Groups[2].Value}";
                var navi = new Button { Content = "Navi", ToolTip = command, HorizontalAlignment = HorizontalAlignment.Left };
                navi.Click += (_, _) => navi.Content = TryCopyToClipboard(command)
                    ? T("Скопировано", "Copied") : T("Не удалось скопировать", "Copy failed");
                panel.Children.Add(navi);
            }
            if (RagnarokPlusBarterCatalog.ReadImage(location.Image) is { } mapImage)
                panel.Children.Add(new Image { Source = DecodeCatalogImage(mapImage), MaxWidth = 320, MaxHeight = 320,
                    Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
        }

        panel.Children.Add(Heading(T("Стоимость обмена", "Exchange cost"), 16));
        foreach (var cost in offer.Costs)
        {
            var item = cost.Kind == "item" ? FindBarterItem(cost.ItemId, cost.Name) : null;
            var label = cost.Kind == "zeny" ? $"{cost.Quantity:N0} Zeny" :
                $"{cost.Name} ×{(cost.Quantity?.ToString("N0") ?? "?")}";
            AddBarterLine(panel, label, cost.Icon, 0, cost.ItemId ?? item?.Id);
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
        panel.Children.Add(add);
        panel.Children.Add(notice);
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

    private void AddBarterLine(Panel panel, string label, string? icon, double headingSize, int? itemId = null)
    {
        var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        if (RagnarokPlusBarterCatalog.ReadImage(icon) is { } image)
            line.Children.Add(new Image { Source = DecodeCatalogImage(image), Width = 32, Height = 32,
                Margin = new Thickness(0, 0, 6, 0), Stretch = Stretch.Uniform });
        if (itemId is int id && _database.Item(id) is not null)
        {
            var link = new Hyperlink(new Run(label)) { FontWeight = FontWeights.Bold, TextDecorations = TextDecorations.Underline };
            link.SetResourceReference(TextElement.ForegroundProperty, "RoDeepBlueBrush");
            link.Click += (_, _) => OpenItem(id);
            var text = Meta("");
            text.Inlines.Add(link);
            line.Children.Add(text);
        }
        else line.Children.Add(headingSize > 0 ? Heading(label, headingSize) : Meta(label));
        panel.Children.Add(line);
    }
}
