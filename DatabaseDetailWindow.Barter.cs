using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.RegularExpressions;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    private void ShowBarter(RagnarokPlusBarterCatalog.Offer offer)
    {
        var reward = FindBarterItem(offer.ItemId, offer.Name);
        AddEntityHeader(offer.Name, offer.ItemId is int id ? $"{offer.Npc} · #{id}" : offer.Npc,
            RagnarokPlusBarterCatalog.ReadImage(offer.Icon) ?? reward?.LocalImagePng, "⇄");
        AddSection(T("Получаете", "You receive"));
        AddReferenceRow(offer.Name, reward is not null ? $"#{reward.Id}" : "",
            offer.Quantity is long count ? $"×{count:N0}" : "—",
            RagnarokPlusBarterCatalog.ReadImage(offer.Icon) ?? reward?.LocalImagePng, "◇",
            reward is null ? null : () => OpenItem(reward.Id));
        AddBarterSection(offer, DetailsPanel);
        if (!string.IsNullOrWhiteSpace(offer.Description) || !string.IsNullOrWhiteSpace(offer.Details))
            CollapsedSection(T("Описание и характеристики", "Description and stats"), Body(
                string.Join("\n\n", new[] { offer.Description, offer.Details }.Where(x => !string.IsNullOrWhiteSpace(x)))));
    }

    private void AddBarterSection(RagnarokPlusBarterCatalog.Offer offer, Panel panel)
    {
        AddBarterHeading(panel, T("Требуется", "Required"));
        var costs = offer.Costs.Select(cost => (Cost: cost, Item: cost.Kind == "item" ? FindBarterItem(cost.ItemId, cost.Name) : null)).ToArray();
        foreach (var (cost, item) in costs)
        {
            var id = item?.Id ?? cost.ItemId;
            AddReferenceRow(cost.Kind == "zeny" ? "Zeny" : cost.Name, id is int known ? $"#{known}" : "",
                cost.Quantity?.ToString("N0") ?? "—", RagnarokPlusBarterCatalog.ReadImage(cost.Icon) ?? item?.LocalImagePng,
                cost.Kind == "zeny" ? "" : "◇", item is null ? null : () => OpenItem(item.Id), panel);
        }

        var materials = costs.Where(row => row.Cost.Kind == "item").Select(row =>
            (Id: row.Item?.Id ?? row.Cost.ItemId ?? 0, row.Cost.Name, Target: row.Cost.Quantity ?? 0)).ToArray();
        var canImport = materials.Length > 0 && materials.All(cost => cost.Id > 0 && cost.Target > 0);
        var add = new Button { Content = T("Добавить материалы в список добычи", "Add materials to loot list"), IsEnabled = canImport,
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
        AddBarterHeading(panel, T("Где обменять", "Where to exchange"));
        AddBarterLine(panel, offer.Npc, offer.NpcIcon, 16);
        foreach (var location in offer.Maps)
        {
            var mapLabel = Meta(location.Code);
            mapLabel.Margin = new Thickness(0, 5, 0, 2);
            panel.Children.Add(mapLabel);
            foreach (var pin in location.Pins)
            {
                var coordinates = Regex.Match(pin, @"(\d+)\s*,\s*(\d+)\s*$");
                var command = coordinates.Success && !string.IsNullOrWhiteSpace(location.Code)
                    ? $"/navi {location.Code} {coordinates.Groups[1].Value}/{coordinates.Groups[2].Value}" : null;
                AddNavigationRow(panel, pin, command);
            }
            if (RagnarokPlusBarterCatalog.ReadImage(location.Image) is { } mapImage)
                panel.Children.Add(new Image { Source = DecodeCatalogImage(mapImage), MaxWidth = 230, MaxHeight = 230,
                    Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 8) });
        }


    }

    private DatabaseItem? FindBarterItem(int? id, string name)
    {
        if (id is int known && _database.Item(known) is { } exact) return exact;
        return RagnarokPlusBarterCatalog.MatchItemName(_database.Items, name);
    }

    private void AddBarterLine(Panel panel, string label, string? icon, double headingSize)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        var image = DatabasePresentation.Icon(RagnarokPlusBarterCatalog.ReadImage(icon), "♟", 18);
        image.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(image);
        var title = Meta(label);
        title.FontSize = headingSize == 0 ? 13 : headingSize;
        title.FontWeight = FontWeights.SemiBold;
        row.Children.Add(title);
        panel.Children.Add(row);
    }

    private void AddBarterHeading(Panel panel, string caption)
    {
        var label = new TextBlock { Text = caption, FontSize = 13, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 15, 0, 5), TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, "DatabaseBlue");
        panel.Children.Add(label);
    }
}
