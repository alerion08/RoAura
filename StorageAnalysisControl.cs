using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public sealed partial class StorageAnalysisControl : UserControl
{
    private sealed record Item(int Id, string Name, string Type, long Count, string Locations,
        long? Npc, long? Vending, StorageBid[] Bids, StorageAnalysis.Entry[] Entries, RagnarokPlusRecipes.Recipe[] Recipes,
        RagnarokPlusBarterCatalog.Offer[] Barters, RagnarokPlusQuestCatalog.Card[] Quests)
    {
        public decimal Total { get; init; }
        public string StockValue => Total.ToString("N0");
        public decimal UnitValue => Count == 0 ? 0 : Total / Count;
        public bool Craft => Recipes.Length + Barters.Length > 0;
        public bool Quest => Quests.Length > 0;
        public string NpcPrice => Npc?.ToString("N0") ?? "—";
        public string VendingPrice => Vending?.ToString("N0") ?? "—";
    }

    private readonly TextBox _search = new() { MinWidth = 160, Margin = new Thickness(4) };
    private readonly ComboBox _filter = new() { SelectedIndex = 0, Margin = new Thickness(4) };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Softcore", "Hardcore" }, SelectedIndex = 0, Margin = new Thickness(4) };
    private readonly ComboBox _types = new() { MinWidth = 112 };
    private readonly StackPanel _details = new();
    private readonly StackPanel _craftDetails = new();
    private readonly System.Windows.Controls.Primitives.UniformGrid _metrics = new() { Rows = 1 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private readonly DataGrid _items = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Extended };
    private readonly TreeView _craft = new();
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
        var title = new TextBlock { FontFamily = new System.Windows.Media.FontFamily("Georgia"), FontSize = 20, FontWeight = FontWeights.SemiBold, Text = T("Анализ хранилища", "Storage analysis") };
        var toolbar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        _load.Content = T("Обновить", "Refresh");
        _search.ToolTip = T("Поиск по названию или ID", "Search by name or ID");
        _filter.ItemsSource = new[] { T("Все предметы", "All items"), T("Можно продать", "Can sell"),
            T("Используется в крафте", "Used in crafting"), T("Используется в задании", "Used in quests") };
        ConfigureToolbar(toolbar);
        toolbar.Children.Add(_mode);
        toolbar.Children.Add(_load);
        DockPanel.SetDock(toolbar, Dock.Right);
        header.Children.Add(toolbar);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        DockTop(root, Surface(header, "RoTitleBrush", 10));
        DockTop(root, _status);
        _status.Visibility = Visibility.Collapsed;

        var sections = new TabControl();
        sections.SetResourceReference(StyleProperty, "SectionTabControlStyle");
        root.Children.Add(sections);
        var analysis = new DockPanel();
        var inventory = new DockPanel();
        ConfigureFilters(inventory);
        inventory.Children.Add(_items);
        analysis.Children.Add(Split(inventory, _details, _items));
        sections.Items.Add(new TabItem { Header = T("Хранилище", "Storage"), Content = analysis });
        sections.Items.Add(new TabItem { Header = T("Мастер крафта", "Crafting assistant"), Content = CraftWorkspace() });

        AddItemColumn();
        AddColumn(T("Кол-во", "Count"), nameof(Item.Count), DataGridLength.Auto);
        AddColumn(T("Склады", "Storage"), nameof(Item.Locations), DataGridLength.Auto);
        AddColumn(T("Весь запас", "Stock value"), nameof(Item.StockValue), DataGridLength.Auto);
        AddColumn(T("NPC / шт.", "NPC / unit"), nameof(Item.NpcPrice), DataGridLength.Auto);
        AddColumn(T("Vending / шт.", "Vending / unit"), nameof(Item.VendingPrice), DataGridLength.Auto);
        ConfigureGrid();
        _load.Click += Load_Click;
        _mode.SelectionChanged += (_, _) => Rebuild();
        _search.TextChanged += (_, _) => Filter();
        _filter.SelectionChanged += (_, _) => Filter();
        _types.SelectionChanged += (_, _) => Filter();
        _items.SelectionChanged += (_, _) => ShowItem(_items.SelectedItem as Item);
        _craft.SelectedItemChanged += (_, _) => ShowCraft((_craft.SelectedItem as TreeViewItem)?.Tag as StorageAnalysis.Craft);
        Loaded += (_, _) => LoadPreferences();
        Unloaded += (_, _) => _request?.Cancel();
    }

    private string T(string ru, string en) => (DataContext as AppSettings)?.Language == "en" ||
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? en : ru;

    private void AddColumn(string title, string property, DataGridLength width) =>
        _items.Columns.Add(new DataGridTextColumn { Header = title, Binding = new System.Windows.Data.Binding(property) { Mode = System.Windows.Data.BindingMode.OneWay }, Width = width, SortMemberPath = property switch { nameof(Item.StockValue) => nameof(Item.Total), nameof(Item.NpcPrice) => nameof(Item.Npc), nameof(Item.VendingPrice) => nameof(Item.Vending), _ => property } });

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app || app.StorageBrowser is not { } browser || _request is not null) return;
        using var request = new CancellationTokenSource();
        _request = request;
        _load.IsEnabled = false;
        _status.Visibility = Visibility.Visible;
        _status.Text = T("Читаем хранилища Ragnarok+…", "Reading Ragnarok+ storage…");
        try
        {
            var entries = await browser.ReadStorageAsync(request.Token, new Progress<string>(message =>
            {
                if (_request != request) return;
                _session.Text = T("Сессия подтверждена", "Session confirmed");
                _status.Text = T($"Читаем данные · {message}", $"Reading data · {message}");
            }));
            request.Token.ThrowIfCancellationRequested();
            _itemIcons.Clear();
            _database = new GameDatabaseService(GameServerProfile.RagnarokPlus);
            _entries = entries;
            RecordSnapshot(entries);
            _session.Text = T("Сессия подтверждена", "Session confirmed");
            _login.Visibility = Visibility.Collapsed;
            Rebuild();
            var snapshot = RagnarokPlusData.LoadMarket();
            _lastImport.ToolTip = snapshot is null
                ? T("Данные собраны. Цены Vending отсутствуют в кеше.", "Storage loaded. Vending price cache is empty.")
                : T($"Данные собраны. Цены Vending из кеша от {snapshot.CapturedUtc.ToLocalTime():g}.",
                    $"Storage loaded. Vending prices cached at {snapshot.CapturedUtc.ToLocalTime():g}.");
            _status.Visibility = Visibility.Collapsed;
        }
        catch (RagnarokPlusSignInRequiredException)
        {
            _login.Visibility = Visibility.Visible;
            _session.Text = T("Требуется вход", "Sign-in required");
            _status.Text = T("Войдите в Ragnarok+ и снова нажмите «Обновить».", "Sign in to Ragnarok+ and click Refresh again.");
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
        var listings = (RagnarokPlusData.LoadMarket()?.Listings ?? []).Where(row => row.GameMode == mode).ToArray();
        var bids = BuildBids(listings);
        var vending = listings.Where(row => row.ShopType == "SELL")
            .Select(row => (row.ItemId, Price: SpringRoMarketService.ParsePrice(row.Price)))
            .Where(row => row.Price is > 0 and < long.MaxValue).GroupBy(row => row.ItemId)
            .ToDictionary(group => group.Key, group => group.Min(row => row.Price));
        _marketAsks = vending;
        var costIds = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        int? ResolveCost(RagnarokPlusBarterCatalog.Cost cost)
        {
            if (cost.ItemId is > 0) return cost.ItemId;
            if (!costIds.TryGetValue(cost.Name, out var id))
                costIds[cost.Name] = id = RagnarokPlusBarterCatalog.MatchItemName(_database.Items, cost.Name)?.Id;
            return id;
        }
        _resolveCost = ResolveCost;
        var barters = RagnarokPlusBarterCatalog.All.SelectMany(offer => offer.Costs.Where(cost => cost.Kind == "item")
            .Select(cost => (Id: ResolveCost(cost), Offer: offer))).Where(row => row.Id.HasValue).ToLookup(x => x.Id!.Value, x => x.Offer);
        _summary = _entries.Where(row => row.Mode == mode).GroupBy(row => row.ItemId).Select(group =>
        {
            var card = _database.Item(group.Key);
            var entry = group.First();
            var item = new Item(group.Key, card?.Name ?? entry.Name ?? $"#{group.Key}", Category(entry.Type ?? card?.Type),
                group.Sum(entry => entry.Amount), string.Join(", ", group.Select(entry => entry.Slot).Distinct().Order()),
                card is null || (!card.RagnarokPlusDetailsCaptured && card.NpcSellPrice == 0) ? null : NpcWithOvercharge(card.NpcSellPrice),
                vending.TryGetValue(group.Key, out var price) ? price : null, bids[group.Key].ToArray(), group.ToArray(),
                RagnarokPlusRecipes.UsingItem(group.Key).ToArray(), barters[group.Key].DistinctBy(o => (o.ShopId, o.Ordinal)).ToArray(),
                RagnarokPlusQuestCatalog.Instance.RequiredFor(GameServerProfile.RagnarokPlus, group.Key).ToArray());
            return item with { Total = Value(item, item.Count) };
        }).OrderBy(item => item.Name).ToArray();
        var selectedType = (_types.SelectedItem as ComboBoxItem)?.Tag as string;
        var types = _summary.Select(item => item.Type).Distinct().Order().ToArray();
        _updatingTypes = true;
        _types.Items.Clear();
        _types.Items.Add(new ComboBoxItem { Content = T("Все типы", "All types") });
        foreach (var type in types)
            _types.Items.Add(new ComboBoxItem { Content = type, Tag = type });
        _types.SelectedIndex = Array.IndexOf(types, selectedType) + 1;
        _updatingTypes = false;
        RefreshCrafts();
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

    private void ShowItem(Item? item)
    {
        _details.Children.Clear();
        if (item is null) { Text(_details, T("Выберите предмет в списке.", "Select a storage item.")); return; }
        ShowItemExtras(item);
        Section(_details, T("По складам", "By storage"));
        foreach (var slot in item.Entries.GroupBy(entry => entry.Slot).OrderBy(group => group.Key))
        {
            var count = slot.Sum(entry => entry.Amount);
            Text(_details, T($"Хранилище {slot.Key} · {count:N0} · {(decimal)count / item.Count:P0}",
                $"Storage {slot.Key} · {count:N0} · {(decimal)count / item.Count:P0}"));
        }
        Section(_details, T("Источники цены", "Price sources"));
        Text(_details, $"NPC · {item.NpcPrice}    Vending · {item.VendingPrice}");
        if (_valuation.SelectedIndex == 1 && item.Bids.Length != 0)
            Text(_details, T($"Скупка · до {item.Bids.Max(bid => bid.Price):N0} z / шт.", $"Buy orders · up to {item.Bids.Max(bid => bid.Price):N0} z / unit"));
        if (item.Craft) Section(_details, T("Рецепты и обмены", "Recipes and barter"));
        foreach (var recipe in item.Recipes.DistinctBy(r => r.ItemId)) Link(_details, recipe.Name, recipe.ItemId);
        foreach (var barter in item.Barters) Link(_details, $"{barter.Name} · {barter.Npc}", barter);
        if (item.Quest) Section(_details, T("Задания", "Quests"));
        foreach (var quest in item.Quests)
        {
            var button = new Button { Content = quest.Title };
            button.Click += async (_, _) => { if (Application.Current is App app) await app.OpenQuestFromDatabaseAsync(quest.Key); };
            _details.Children.Add(button);
        }
        if (_database?.Item(item.Id)?.Description is { Length: > 0 } description)
        {
            var body = new StackPanel();
            Text(body, description);
            _details.Children.Add(new Expander { Header = T("Описание", "Description"), Content = body });
        }
        var history = new StackPanel();
        ShowHistory(item, history);
        if (history.Children.Count > 0)
            _details.Children.Add(new Expander { Header = T("История остатка", "Stock history"), Content = history });
    }

    private void ShowCraft(StorageAnalysis.Craft? craft)
    {
        _craftDetails.Children.Clear();
        if (craft is null) { Text(_craftDetails, T("Выберите рецепт в списке.", "Select a recipe.")); return; }
        var header = new DockPanel();
        AddCardIcon(header, craft.ResultId ?? 0);
        var title = new StackPanel();
        Heading(title, craft.Name);
        header.Children.Add(title);
        _craftDetails.Children.Add(header);
        Tiles(_craftDetails, (T("Готовность", "Readiness"), $"{craft.Percent}%"),
            (T("Можно выполнить", "Operations"), $"{craft.Repeats:N0}"),
            (T("Выход за операцию", "Output per operation"), craft.Output is { } output ? $"{output:N0}" : "—"));
        Section(_craftDetails, T("Материалы на одну операцию", "Materials per operation"));
        foreach (var part in craft.Materials)
        {
            Link(_craftDetails, part.Name, part.ItemId);
            Text(_craftDetails, T($"Нужно {part.Required:N0} · Есть {part.Available:N0} · Не хватает {part.Missing:N0}",
                $"Required {part.Required:N0} · In storage {part.Available:N0} · Missing {part.Missing:N0}"));
            ShowWithdrawals(part);
        }
        Section(_craftDetails, T("Стоимость", "Cost"));
        ShowCraftEconomics(craft);
        Link(_craftDetails, T("Открыть рецепт или обмен ↗", "Open recipe or barter ↗"), craft.Target);
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
