using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    internal Func<IReadOnlyList<MarketListing>>? GetSpringFarmOffers { get; set; }

    private static Border CraftTag(string label)
    {
        var tag = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.Normal } };
        tag.SetResourceReference(Border.BorderBrushProperty, "RoBorderBrush");
        return tag;
    }

    private void AddFarmPlanner(DatabaseMap map)
    {
        var monsters = map.Monsters.OrderByDescending(row => ParseCount(row.Count))
            .Select(row => (Spawn: row, Monster: _database.Monster(row.MonsterId)))
            .Where(row => row.Monster is not null)
            .DistinctBy(row => row.Spawn.MonsterId).ToArray();
        if (monsters.Length == 0) return;

        var planner = new Expander { Header = T("План фарма", "Farm plan"), Margin = new Thickness(0, 8, 0, 5) };
        var form = new StackPanel { Margin = new Thickness(2, 6, 2, 6) };
        var monsterPicker = new ComboBox
        {
            ItemsSource = monsters.Select(row => SpawnLabel(row.Spawn.Name, row.Spawn.Count, row.Spawn.Respawn)).ToArray(),
            SelectedIndex = 0
        };
        var targets = new Dictionary<int, TextBox>();
        var targetPanel = new StackPanel();
        var add = new Button { Content = T("Добавить", "Add") };
        var priceMode = new ComboBox
        {
            ItemsSource = new[] { "NPC", T("Скупка (кэш)", "Buy orders (cached)"), T("Продажа (кэш)", "Sell listings (cached)"),
                T("Продажа с запасной ценой NPC", "Sell listings with NPC fallback") },
            SelectedIndex = GameServerProfile.SupportsMarket(_database.Version) ? 3 : 0,
            IsEnabled = GameServerProfile.SupportsMarket(_database.Version)
        };
        var result = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var attach = new Button { Content = T("Сохранить план в текущей сессии", "Save plan to current session"), IsEnabled = false };
        var status = Meta("");
        var pickRow = new DockPanel();
        DockPanel.SetDock(add, Dock.Right);
        pickRow.Children.Add(add);
        pickRow.Children.Add(monsterPicker);
        form.Children.Add(pickRow);
        form.Children.Add(targetPanel);
        form.Children.Add(Meta(T("Источник оценки", "Price source")));
        form.Children.Add(priceMode);
        form.Children.Add(attach);
        form.Children.Add(status);
        form.Children.Add(result);
        planner.Content = form;
        DetailsPanel.Children.Add(planner);

        Dictionary<(int Id, string Side), long> prices = [];
        FarmingPlan? currentPlan = null;
        var server = _database.Version;
        planner.Expanded += (_, _) =>
        {
            var offers = server == GameServerProfile.RagnarokPlus
                ? (RagnarokPlusData.LoadMarket()?.Listings ?? []).Where(row => row is not null && row.GameMode == "SC")
                : server == GameServerProfile.SpringRo ? GetSpringFarmOffers?.Invoke() ?? [] : [];
            prices = offers.Where(row => row is not null && row.ShopType is "BUY" or "SELL")
                .Select(row => (Key: (row.ItemId, row.ShopType), Price: SpringRoMarketService.ParsePrice(row.Price ?? "")))
                .Where(row => row.Price is > 0 and < long.MaxValue)
                .GroupBy(row => row.Key).ToDictionary(group => group.Key, group =>
                    group.Key.ShopType == "BUY" ? group.Max(row => row.Price) : group.Min(row => row.Price));
            Update();
        };
        add.Click += (_, _) => AddTarget(monsterPicker.SelectedIndex);
        priceMode.SelectionChanged += (_, _) => Update();
        attach.Click += (_, _) =>
        {
            if (currentPlan is null) return;
            status.Text = (Application.Current as App)?.AttachFarmingPlan(server, currentPlan) == true
                ? T("План сохранён в текущей сессии.", "Plan saved to the current session.")
                : T("Запустите сессию на этом сервере.", "Start a session on this server.");
        };
        AddTarget(0);

        void AddTarget(int index)
        {
            if (index < 0) return;
            if (targets.TryGetValue(index, out var existing)) { existing.Focus(); return; }
            var row = new DockPanel();
            var kills = new TextBox { Text = "100", Width = 65, ToolTip = T("Количество убийств", "Kill count") };
            var remove = new Button { Content = "×", ToolTip = T("Убрать монстра", "Remove monster") };
            DockPanel.SetDock(remove, Dock.Right);
            DockPanel.SetDock(kills, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(kills);
            row.Children.Add(Meta(monsters[index].Monster!.Name));
            targetPanel.Children.Add(row);
            targets.Add(index, kills);
            kills.TextChanged += (_, _) => Update();
            remove.Click += (_, _) => { targets.Remove(index); targetPanel.Children.Remove(row); Update(); };
            Update();
        }

        void Update()
        {
            if (!planner.IsExpanded) return;
            result.Children.Clear();
            status.Text = "";
            currentPlan = null;
            attach.IsEnabled = false;
            var planTargets = new List<FarmingPlanTarget>();
            var expectedDrops = new List<(DatabaseDrop Drop, decimal Expected)>();
            foreach (var (index, input) in targets)
            {
                if (!int.TryParse(input.Text, NumberStyles.None, CultureInfo.CurrentCulture, out var count) || count <= 0)
                {
                    result.Children.Add(Meta(T("Укажи целое количество убийств больше нуля.", "Enter a positive whole number of kills.")));
                    return;
                }
                var monster = monsters[index].Monster!;
                planTargets.Add(new(monster.Id, monster.Name, count));
                expectedDrops.AddRange(monster.Drops.Where(drop => !drop.IsMvp && drop.ItemId > 0)
                    .Select(drop => (Drop: drop, Rate: ParseRate(drop.Rate)))
                    .Where(row => row.Rate is > 0 and <= 100)
                    .Select(row => (row.Drop, count * (decimal)row.Rate / 100)));
            }

            if (planTargets.Count == 0) return;
            var drops = expectedDrops.GroupBy(row => row.Drop.ItemId)
                .Select(group => (Drop: group.First().Drop, Expected: (long)decimal.Floor(group.Sum(row => row.Expected))))
                .OrderByDescending(row => row.Expected).ToArray();

            decimal total = 0;
            var missingPrices = 0;
            var planDrops = new List<FarmingPlanDrop>();
            var source = priceMode.SelectedIndex switch { 1 => "BUY", 2 => "SELL", 3 => "AUTO", _ => "NPC" };
            var summary = Meta("");
            result.Children.Add(summary);
            foreach (var (drop, expected) in drops)
            {
                var item = _database.Item(drop.ItemId);
                var side = source == "AUTO" ? "SELL" : source;
                var marketPrice = prices.GetValueOrDefault((drop.ItemId, side));
                var useNpc = source == "NPC" || (source == "AUTO" && marketPrice == 0);
                var price = useNpc ? item?.NpcSellPrice ?? 0 : marketPrice;
                if (price > 0) total += (decimal)expected * price;
                else if (expected > 0) missingPrices++;
                planDrops.Add(new(drop.ItemId, item?.Name ?? drop.Name, expected, Math.Max(0, price)));
                var material = _database.Version == GameServerProfile.RagnarokPlus &&
                    RagnarokPlusRecipes.UsingItem(drop.ItemId).Any();
                var craftable = _database.Version == GameServerProfile.RagnarokPlus &&
                    RagnarokPlusRecipes.ForItem(drop.ItemId).Any();
                var name = item?.Name ?? drop.Name;
                var details = $"×{expected:N0}" + (price > 0 ? $"  ·  {price:N0} z" : "");
                var lines = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
                var title = new DockPanel();
                if (material) AddTag("mats");
                if (craftable) AddTag("craftable");
                title.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
                lines.Children.Add(title);

                var valuationRow = new DockPanel();
                var badge = new ContentControl
                {
                    Content = new { PriceKnown = price > 0, PriceSource = useNpc ? "NPC" : side == "BUY" ? "Buy" : "Vending" },
                    ContentTemplate = (DataTemplate)FindResource("GoldGoblinPriceBadgeTemplate"),
                    FontSize = 10
                };
                DockPanel.SetDock(badge, Dock.Right);
                valuationRow.Children.Add(badge);
                valuationRow.Children.Add(new TextBlock { Text = details, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis });
                lines.Children.Add(valuationRow);

                void AddTag(string label)
                {
                    var tag = CraftTag(label);
                    DockPanel.SetDock(tag, Dock.Right);
                    title.Children.Add(tag);
                }
                var link = new Button
                {
                    Content = lines, ToolTip = name + "\n" + details,
                    Height = 52, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(0),
                    IsEnabled = item is not null, HorizontalAlignment = HorizontalAlignment.Stretch,
                    Style = (Style)FindResource("FarmResultButtonStyle")
                };
                link.Click += (_, _) => OpenItem(drop.ItemId);
                result.Children.Add(link);
            }
            currentPlan = new FarmingPlan(map.Code, map.Name, source, planTargets.ToArray(), planDrops.ToArray());
            attach.IsEnabled = true;
            var valuation = missingPrices > 0 && total == 0 ? "—" : $"{total:N0} z";
            summary.Text = T($"Оценка дропа: {valuation} · без цены: {missingPrices}",
                $"Estimated drop value: {valuation} · unpriced: {missingPrices}");
        }
    }
}
