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
        var title = Heading(T("Бартер", "Barter"), 16);
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.Margin = new Thickness(0, 12, 0, 5);
        panel.Children.Add(title);
        if (offer.Quantity > 1) panel.Children.Add(Meta(T($"За обмен: {offer.Quantity:N0}", $"Per exchange: {offer.Quantity:N0}")));
        AddBarterLine(panel, offer.Npc, offer.NpcIcon, 16);
        foreach (var location in offer.Maps)
        {
            var mapLabel = Meta(location.Code);
            mapLabel.Margin = new Thickness(0, 5, 0, 2);
            panel.Children.Add(mapLabel);
            foreach (var pin in location.Pins)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                panel.Children.Add(row);
                var name = Meta(pin);
                name.VerticalAlignment = VerticalAlignment.Center;
                var coordinates = Regex.Match(pin, @"(\d+)\s*,\s*(\d+)\s*$");
                if (!coordinates.Success || string.IsNullOrWhiteSpace(location.Code))
                {
                    row.Children.Add(name);
                    continue;
                }
                var command = $"/navi {location.Code} {coordinates.Groups[1].Value}/{coordinates.Groups[2].Value}";
                var navi = new Button { Content = "Navi", ToolTip = command, Width = 45, Height = 24, MinHeight = 0, Padding = new Thickness(2),
                    Margin = new Thickness(6, 0, 0, 0) };
                navi.Click += (_, _) => navi.ToolTip = TryCopyToClipboard(command)
                    ? T("Скопировано", "Copied") : T("Не удалось скопировать", "Copy failed");
                DockPanel.SetDock(navi, Dock.Right);
                row.Children.Add(navi);
                row.Children.Add(name);
            }
            if (RagnarokPlusBarterCatalog.ReadImage(location.Image) is { } mapImage)
                panel.Children.Add(new Image { Source = DecodeCatalogImage(mapImage), MaxWidth = 320, MaxHeight = 320,
                    Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 8) });
        }

        var costHeading = Heading(T("Стоимость обмена", "Exchange cost"), 16);
        costHeading.HorizontalAlignment = HorizontalAlignment.Left;
        costHeading.Margin = new Thickness(0, 6, 0, 4);
        panel.Children.Add(costHeading);
        var costs = offer.Costs.Select(cost => (Cost: cost, Item: cost.Kind == "item" ? FindBarterItem(cost.ItemId, cost.Name) : null)).ToArray();
        foreach (var (cost, item) in costs)
        {
            var label = cost.Kind == "zeny" ? $"{cost.Quantity:N0} Zeny" :
                $"{cost.Name} ×{(cost.Quantity?.ToString("N0") ?? "?")}";
            AddBarterLine(panel, label, cost.Icon, 0, item?.Id ?? cost.ItemId);
        }

        var materials = costs.Where(row => row.Cost.Kind == "item").Select(row =>
            (Id: row.Item?.Id ?? row.Cost.ItemId ?? 0, row.Cost.Name, Target: row.Cost.Quantity ?? 0)).ToArray();
        var canImport = materials.Length > 0 && materials.All(cost => cost.Id > 0 && cost.Target > 0);
        var add = new Button { Content = T("Добавить в список добычи", "Add to loot list"), IsEnabled = canImport,
            Margin = new Thickness(0, 8, 0, 4) };
        var notice = Meta(canImport ? "" : T("Для списка добычи нужны ID и количество каждого материала.", "Loot lists require an ID and quantity for every material."));
        add.Click += (_, _) =>
        {
            var targets = materials.GroupBy(cost => cost.Id).Select(group =>
                (group.Key, group.First().Name, group.Sum(cost => cost.Target)));
            LootLists.ImportQuest(_settings, GameServerProfile.RagnarokPlus, $"barter:{offer.ShopId}:{offer.Ordinal}",
                offer.Name, targets, reuseUnlinkedList: false);
            (Application.Current as App)?.LootListsChanged();
            notice.Visibility = Visibility.Visible;
            notice.Text = T("Материалы добавлены в список добычи.", "Materials added to the loot list.");
        };
        panel.Children.Add(add);
        notice.Visibility = canImport ? Visibility.Collapsed : Visibility.Visible;
        panel.Children.Add(notice);
    }

    private DatabaseItem? FindBarterItem(int? id, string name)
    {
        if (id is int known && _database.Item(known) is { } exact) return exact;
        return RagnarokPlusBarterCatalog.MatchItemName(_database.Items, name);
    }

    private void AddBarterLine(Panel panel, string label, string? icon, double headingSize, int? itemId = null)
    {
        var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        if (RagnarokPlusBarterCatalog.ReadImage(icon) is { } image)
            line.Children.Add(new Image { Source = DecodeCatalogImage(image), Width = headingSize > 0 ? 20 : 16, Height = headingSize > 0 ? 20 : 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0), Stretch = Stretch.Uniform });
        if (itemId is int id && _database.Item(id) is not null)
        {
            var link = new Hyperlink(new Run(label)) { FontWeight = FontWeights.Bold, TextDecorations = TextDecorations.Underline };
            link.SetResourceReference(TextElement.ForegroundProperty, "RoDeepBlueBrush");
            link.Click += (_, _) => OpenItem(id);
            var text = Meta("");
            text.Margin = new Thickness(0);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Inlines.Add(link);
            line.Children.Add(text);
        }
        else
        {
            var text = headingSize > 0 ? Heading(label, headingSize) : Meta(label);
            text.HorizontalAlignment = HorizontalAlignment.Left;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(0);
            line.Children.Add(text);
        }
        panel.Children.Add(line);
    }
}
