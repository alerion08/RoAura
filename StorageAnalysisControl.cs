using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public sealed class StorageAnalysisControl : UserControl
{
    private sealed record Item(int Id, string Name, string Type, long Count, string Locations,
        long? Npc, long? Vending, StorageAnalysis.Entry[] Entries, RagnarokPlusRecipes.Recipe[] Recipes,
        RagnarokPlusBarterCatalog.Offer[] Barters, RagnarokPlusQuestCatalog.Card[] Quests)
    {
        public decimal Total => Count * (decimal)Math.Max(Npc ?? 0, Vending ?? 0);
        public bool Craft => Recipes.Length + Barters.Length > 0;
        public bool Quest => Quests.Length > 0;
        public string NpcPrice => Npc?.ToString("N0") ?? "—";
        public string VendingPrice => Vending?.ToString("N0") ?? "—";
    }

    private readonly TextBox _search = new() { MinWidth = 160, Margin = new Thickness(4) };
    private readonly ComboBox _filter = new() { SelectedIndex = 0, Margin = new Thickness(4) };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Softcore", "Hardcore" }, SelectedIndex = 0, Margin = new Thickness(4) };
    private readonly TabControl _types = new();
    private readonly StackPanel _details = new();
    private readonly StackPanel _craftDetails = new();
    private readonly System.Windows.Controls.Primitives.UniformGrid _metrics = new() { Rows = 1 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private readonly DataGrid _items = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
    private readonly ListBox _craft = new() { DisplayMemberPath = "Name" };
    private readonly Button _load = new();
    private StorageAnalysis.Entry[] _entries = [];
    private Item[] _summary = [];
    private GameDatabaseService? _database;
    private CancellationTokenSource? _request;

    public StorageAnalysisControl()
    {
        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;
        var header = new DockPanel();
        var title = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, Text = T("Анализ хранилища", "Storage analysis") };
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var toolbar = new WrapPanel();
        _load.Content = T("Собрать данные", "Read storage");
        _search.ToolTip = T("Поиск по названию или ID", "Search by name or ID");
        _filter.ItemsSource = new[] { T("Все предметы", "All items"), T("Можно продать", "Can sell"),
            T("Используется в крафте", "Used in crafting"), T("Используется в задании", "Used in quests") };
        toolbar.Children.Add(_load);
        toolbar.Children.Add(_mode);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);
        DockPanel.SetDock(_metrics, Dock.Top);
        root.Children.Add(_metrics);

        var sections = new TabControl();
        sections.SetResourceReference(StyleProperty, "SectionTabControlStyle");
        root.Children.Add(sections);
        var analysis = new DockPanel();
        var filters = new WrapPanel();
        filters.Children.Add(new TextBlock { Text = T("Поиск", "Search"), VerticalAlignment = VerticalAlignment.Center });
        filters.Children.Add(_search);
        filters.Children.Add(_filter);
        DockPanel.SetDock(filters, Dock.Top);
        analysis.Children.Add(filters);
        _types.SetResourceReference(StyleProperty, "SectionTabControlStyle");
        DockPanel.SetDock(_types, Dock.Top);
        analysis.Children.Add(_types);
        analysis.Children.Add(Split(_items, _details));
        sections.Items.Add(new TabItem { Header = T("Анализ хранилища", "Storage analysis"), Content = analysis });
        sections.Items.Add(new TabItem { Header = T("Мастер крафта", "Crafting assistant"), Content = Split(_craft, _craftDetails) });

        AddColumn(T("Предмет", "Item"), nameof(Item.Name), new DataGridLength(2, DataGridLengthUnitType.Star));
        AddColumn(T("Количество", "Count"), nameof(Item.Count), DataGridLength.Auto);
        AddColumn(T("Хранилища", "Storage"), nameof(Item.Locations), DataGridLength.Auto);
        AddColumn(T("Цена NPC", "NPC price"), nameof(Item.NpcPrice), DataGridLength.Auto);
        AddColumn(T("Цена Vending", "Vending price"), nameof(Item.VendingPrice), DataGridLength.Auto);
        _items.Columns.Add(new DataGridCheckBoxColumn { Header = T("Крафт", "Craft"), Binding = new System.Windows.Data.Binding(nameof(Item.Craft)) });
        _items.Columns.Add(new DataGridCheckBoxColumn { Header = T("Задание", "Quest"), Binding = new System.Windows.Data.Binding(nameof(Item.Quest)) });
        _load.Click += Load_Click;
        _mode.SelectionChanged += (_, _) => Rebuild();
        _search.TextChanged += (_, _) => Filter();
        _filter.SelectionChanged += (_, _) => Filter();
        _types.SelectionChanged += (_, _) => Filter();
        _items.SelectionChanged += (_, _) => ShowItem(_items.SelectedItem as Item);
        _craft.SelectionChanged += (_, _) => ShowCraft(_craft.SelectedItem as StorageAnalysis.Craft);
        Unloaded += (_, _) => _request?.Cancel();
    }

    private string T(string ru, string en) => (DataContext as AppSettings)?.Language == "en" ||
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? en : ru;

    private static Grid Split(UIElement left, StackPanel right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridLengthUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220), MinWidth = 170 });
        grid.Children.Add(left);
        var scroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        return grid;
    }

    private void AddColumn(string title, string property, DataGridLength width) =>
        _items.Columns.Add(new DataGridTextColumn { Header = title, Binding = new System.Windows.Data.Binding(property), Width = width });

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app || app.StorageBrowser is not { } browser || _request is not null) return;
        using var request = new CancellationTokenSource();
        _request = request;
        _load.IsEnabled = false;
        _status.Text = T("Читаем хранилища Ragnarok+…", "Reading Ragnarok+ storage…");
        try
        {
            var entries = await browser.ReadStorageAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            _database = new GameDatabaseService(GameServerProfile.RagnarokPlus);
            _entries = entries;
            Rebuild();
            var snapshot = RagnarokPlusData.LoadMarket();
            _status.Text = snapshot is null
                ? T("Данные собраны. Цены Vending отсутствуют в кеше.", "Storage loaded. Vending price cache is empty.")
                : T($"Данные собраны. Цены Vending из кеша от {snapshot.CapturedUtc.ToLocalTime():g}.",
                    $"Storage loaded. Vending prices cached at {snapshot.CapturedUtc.ToLocalTime():g}.");
        }
        catch (RagnarokPlusSignInRequiredException)
        {
            _status.Text = T("Войдите в Ragnarok+ и снова нажмите «Собрать данные».", "Sign in to Ragnarok+ and click Read storage again.");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            DebugLog.Write($"RAGNAROK+ STORAGE READ ERROR: {ex.GetType().Name}");
            _status.Text = T("Не удалось получить данные. Проверьте вход и привязку игрового аккаунта на Ragnarok+.",
                "Could not read storage. Check sign-in and verify your game account is linked on Ragnarok+.");
        }
        finally
        {
            _request = null;
            _load.IsEnabled = true;
        }
    }

    private void Rebuild()
    {
        if (_database is null) return;
        var mode = _mode.SelectedIndex == 1 ? "HC" : "SC";
        var vending = (RagnarokPlusData.LoadMarket()?.Listings ?? []).Where(row => row.ShopType == "SELL" && row.GameMode == mode)
            .Select(row => (row.ItemId, Price: SpringRoMarketService.ParsePrice(row.Price)))
            .Where(row => row.Price is > 0 and < long.MaxValue).GroupBy(row => row.ItemId)
            .ToDictionary(group => group.Key, group => group.Min(row => row.Price));
        var costIds = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        int? ResolveCost(RagnarokPlusBarterCatalog.Cost cost)
        {
            if (cost.ItemId is > 0) return cost.ItemId;
            if (!costIds.TryGetValue(cost.Name, out var id))
                costIds[cost.Name] = id = RagnarokPlusBarterCatalog.MatchItemName(_database.Items, cost.Name)?.Id;
            return id;
        }
        var barters = RagnarokPlusBarterCatalog.All.SelectMany(offer => offer.Costs.Where(cost => cost.Kind == "item")
            .Select(cost => (Id: ResolveCost(cost), Offer: offer))).Where(row => row.Id.HasValue).ToLookup(x => x.Id!.Value, x => x.Offer);
        _summary = _entries.Where(row => row.Mode == mode).GroupBy(row => row.ItemId).Select(group =>
        {
            var card = _database.Item(group.Key);
            var entry = group.First();
            return new Item(group.Key, card?.Name ?? entry.Name ?? $"#{group.Key}", Category(entry.Type ?? card?.Type),
                group.Sum(entry => entry.Amount), string.Join(", ", group.Select(entry => entry.Slot).Distinct().Order()),
                card is null || (!card.RagnarokPlusDetailsCaptured && card.NpcSellPrice == 0) ? null : card.NpcSellPrice,
                vending.TryGetValue(group.Key, out var price) ? price : null, group.ToArray(),
                RagnarokPlusRecipes.UsingItem(group.Key).ToArray(), barters[group.Key].DistinctBy(o => (o.ShopId, o.Ordinal)).ToArray(),
                RagnarokPlusQuestCatalog.Instance.RequiredFor(GameServerProfile.RagnarokPlus, group.Key).ToArray());
        }).OrderBy(item => item.Name).ToArray();
        _types.Items.Clear();
        _types.Items.Add(new TabItem { Header = T("Все типы", "All types") });
        foreach (var type in _summary.Select(item => item.Type).Distinct().Order())
            _types.Items.Add(new TabItem { Header = type, Tag = type });
        _types.SelectedIndex = 0;
        var most = _summary.MaxBy(item => item.Count);
        var richest = _summary.Where(item => item.Total > 0).MaxBy(item => item.Total);
        _metrics.Children.Clear();
        Metric(T("Всего предметов", "Total items"), $"{_summary.Sum(item => item.Count):N0}");
        Metric(T("Оценка стоимости", "Estimated value"), $"{_summary.Sum(item => item.Total):N0} z");
        Metric(T("Топ по количеству", "Most numerous"), most is null ? "—" : $"{most.Name} · {most.Count:N0}");
        Metric(T("Топ по стоимости", "Highest value"), richest is null ? "—" : $"{richest.Name} · {richest.Total:N0} z");
        _craft.ItemsSource = StorageAnalysis.Plans(_summary.ToDictionary(item => item.Id, item => item.Count), ResolveCost);
        Filter();
    }

    private string Category(string? type) => type?.ToLowerInvariant() switch
    {
        "healing" or "usable" or "delayconsume" or "consumable" => T("Расходники", "Usable"),
        "cash" => T("Премиум", "Cash"),
        "armor" or "shadowgear" or "petarmor" or "petegg" or "equipment" => T("Экипировка", "Equipment"),
        "weapon" => T("Оружие", "Weapons"),
        "costume" => T("Костюмы", "Costumes"),
        "ammo" => T("Боеприпасы", "Ammo"),
        "card" => T("Карты", "Cards"),
        _ => T("Другое", "Other")
    };

    private void Metric(string heading, string value)
    {
        var contents = new StackPanel();
        contents.Children.Add(new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap });
        contents.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        var card = new Border { Child = contents, CornerRadius = new CornerRadius(6), Margin = new Thickness(4), Padding = new Thickness(10) };
        card.SetResourceReference(Border.BackgroundProperty, "RoPanelBrush");
        _metrics.Children.Add(card);
    }

    private void Filter()
    {
        var query = _search.Text.Trim();
        var type = (_types.SelectedItem as TabItem)?.Tag as string;
        _items.ItemsSource = _summary.Where(item => (type is null || item.Type == type) &&
            (item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || item.Id.ToString() == query.TrimStart('#')) &&
            (_filter.SelectedIndex switch { 1 => !item.Craft && !item.Quest, 2 => item.Craft, 3 => item.Quest, _ => true })).ToArray();
    }

    private void ShowItem(Item? item)
    {
        _details.Children.Clear();
        if (item is null) return;
        Text(_details, item.Name, true);
        Link(_details, T("Открыть карточку предмета", "Open item card"), item.Id);
        Text(_details, _database?.Item(item.Id)?.Description ?? "");
        foreach (var slot in item.Entries.GroupBy(entry => entry.Slot).OrderBy(group => group.Key))
            Text(_details, T($"Хранилище {slot.Key} · {slot.Sum(entry => entry.Amount):N0}", $"Storage {slot.Key} · {slot.Sum(entry => entry.Amount):N0}"));
        foreach (var recipe in item.Recipes.DistinctBy(r => r.ItemId)) Link(_details, recipe.Name, recipe.ItemId);
        foreach (var barter in item.Barters) Link(_details, $"{barter.Name} · {barter.Npc}", barter);
        foreach (var quest in item.Quests)
        {
            var button = new Button { Content = quest.Title };
            button.Click += async (_, _) => { if (Application.Current is App app) await app.OpenQuestFromDatabaseAsync(quest.Key); };
            _details.Children.Add(button);
        }
    }

    private void ShowCraft(StorageAnalysis.Craft? craft)
    {
        _craftDetails.Children.Clear();
        if (craft is null) return;
        Text(_craftDetails, $"{craft.Name} · {craft.Percent}%", true);
        if (craft.Target is RagnarokPlusBarterCatalog.Offer offer)
            foreach (var fee in offer.Costs.Where(cost => cost.Kind == "zeny"))
                Text(_craftDetails, $"{fee.Quantity:N0} Zeny");
        Link(_craftDetails, T("Открыть рецепт или обмен", "Open recipe or barter"), craft.Target);
        foreach (var part in craft.Materials)
        {
            Link(_craftDetails, part.Name, part.ItemId);
            Text(_craftDetails, T($"Нужно {part.Required:N0} · Есть {part.Available:N0} · Не хватает {part.Missing:N0}",
                $"Required {part.Required:N0} · In storage {part.Available:N0} · Missing {part.Missing:N0}"));
        }
    }

    private static void Text(Panel panel, string value, bool bold = false) =>
        panel.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 4, 0, 4) });

    private static void Link(Panel panel, string caption, object target)
    {
        var button = new Button { Content = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap } };
        button.Click += (_, _) => (Application.Current as App)?.OpenStorageDatabaseCard(target);
        panel.Children.Add(button);
    }
}
