using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public sealed partial class StorageAnalysisControl
{
    private sealed record StorageBid(long Price, long Quantity);
    private readonly TextBlock _session = new();
    private readonly TextBlock _lastImport = new();
    private readonly TextBlock _filteredTotals = new() { TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _chips = new();
    private readonly TextBox _minimum = new() { Width = 86 };
    private readonly TextBox _maximum = new() { Width = 86 };
    private readonly ComboBox _priceBasis = new();
    private readonly ComboBox _valuation = new();
    private readonly ComboBox _craftGroup = new();
    private readonly TextBox _threshold = new() { Width = 50, Text = "60" };
    private readonly CheckBox _pinnedOnly = new();
    private readonly CheckBox _compact = new();
    private readonly TextBox _tag = new() { Width = 125 };
    private readonly List<(string Property, ListSortDirection Direction)> _sort = [];
    private readonly System.Windows.Threading.DispatcherTimer _ageTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private Func<RagnarokPlusBarterCatalog.Cost, int?>? _resolveCost;
    private bool _updatingTypes;
    private int? _storageSlot;
    private void SavePreferences() => (Window.GetWindow(this) as MainWindow)?.RequestSave();
    private AppSettings? Preferences => DataContext as AppSettings;

    private void LoadPreferences()
    {
        if (Preferences is { } preferences)
        {
            _compact.IsChecked = preferences.StorageCompact;
            _threshold.Text = preferences.StorageCraftThreshold.ToString(CultureInfo.InvariantCulture);
            _overcharge.SelectedIndex = Math.Clamp(preferences.StorageOvercharge, 0, 10);
        }
        ReadHistory();
        UpdateAge();
        _ageTimer.Start();
    }

    private readonly ComboBox _overcharge = new();

    private void ConfigureToolbar(Panel toolbar)
    {
        _session.Text = T("Сессия не проверена", "Session unchecked");
        _session.Margin = new Thickness(6);
        _lastImport.Margin = new Thickness(6);
        _ageTimer.Tick += (_, _) => UpdateAge();
        Unloaded += (_, _) => _ageTimer.Stop();
        var login = Action(T("Войти в Ragnarok+", "Sign in to Ragnarok+"), async () =>
        {
            if (Application.Current is App app && app.StorageBrowser is { } browser)
            {
                try
                {
                    await browser.SignInAsync();
                    _session.Text = T("Вход ожидает проверки", "Sign-in pending verification");
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"RAGNAROK+ STORAGE LOGIN ERROR: {ex.GetType().Name}");
                    _session.Text = T("Не удалось открыть вход", "Could not open sign-in");
                }
            }
        });
        toolbar.Children.Add(login);
        toolbar.Children.Add(_session);
        toolbar.Children.Add(_lastImport);
        _load.SetResourceReference(BackgroundProperty, "RoDeepBlueBrush");
        _load.Foreground = Brushes.White;
    }

    private void ConfigureFilters(DockPanel analysis, Panel filters)
    {
        _priceBasis.ItemsSource = new[] { T("Стоимость запаса", "Stock value"), T("Цена за штуку", "Value per unit") };
        _priceBasis.SelectedIndex = 0;
        _valuation.ItemsSource = new[] { T("NPC или Vending", "NPC or vending"), T("Скупка с остатком в NPC", "Buy orders with NPC fallback") };
        _valuation.SelectedIndex = 0;
        _overcharge.ItemsSource = Enumerable.Range(0, 11).Select(level => T($"Наценка NPC · {level}", $"NPC Overcharge · {level}"));
        _overcharge.SelectedIndex = 0;
        _minimum.ToolTip = T("Минимальная стоимость", "Minimum value");
        _maximum.ToolTip = T("Максимальная стоимость", "Maximum value");
        _pinnedOnly.Content = T("Закреплённые", "Pinned");
        _compact.Content = T("Компактно", "Compact");
        foreach (var control in new Control[] { _priceBasis, _minimum, _maximum, _valuation, _overcharge, _pinnedOnly, _compact, _tag })
            control.Margin = new Thickness(4);
        _tag.ToolTip = T("Тег для выделенных строк", "Tag for selected rows");
        var tools = new WrapPanel();
        foreach (var element in new UIElement[] { _priceBasis, _minimum, _maximum, _valuation, _overcharge, _pinnedOnly, _compact })
            tools.Children.Add(element);
        var tagging = new WrapPanel();
        tagging.Children.Add(_tag);
        tagging.Children.Add(Action(T("Добавить тег", "Add tag"), () => ApplyTag(true)));
        tagging.Children.Add(Action(T("Снять тег", "Remove tag"), () => ApplyTag(false)));
        DockTop(analysis, tools);
        DockTop(analysis, tagging);
        DockTop(analysis, _chips);
        DockPanel.SetDock(_filteredTotals, Dock.Bottom);
        analysis.Children.Add(Surface(_filteredTotals, "RoTitleBrush", 6));
        DockPanel.SetDock(analysis.Children[^1], Dock.Bottom);
        _minimum.TextChanged += (_, _) => Filter();
        _maximum.TextChanged += (_, _) => Filter();
        _priceBasis.SelectionChanged += (_, _) => Filter();
        _valuation.SelectionChanged += (_, _) => Rebuild();
        _overcharge.SelectionChanged += (_, _) =>
        {
            if (Preferences is { } settings && _overcharge.SelectedIndex >= 0)
                { settings.StorageOvercharge = _overcharge.SelectedIndex; SavePreferences(); }
            Rebuild();
        };
        _pinnedOnly.Checked += (_, _) => Filter();
        _pinnedOnly.Unchecked += (_, _) => Filter();
        _compact.Checked += (_, _) => SetCompact(true);
        _compact.Unchecked += (_, _) => SetCompact(false);
    }

    private void SetCompact(bool compact)
    {
        _items.RowHeight = compact ? 23 : 32;
        if (Preferences is { } settings) { settings.StorageCompact = compact; SavePreferences(); }
    }

    private void ConfigureGrid()
    {
        SetCompact(false);
        _items.EnableRowVirtualization = true;
        _items.EnableColumnVirtualization = true;
        _items.Sorting += (_, e) =>
        {
            e.Handled = true;
            var property = e.Column.SortMemberPath;
            if (string.IsNullOrEmpty(property) && e.Column is DataGridBoundColumn { Binding: Binding binding })
                property = binding.Path.Path;
            if (string.IsNullOrEmpty(property)) return;
            var index = _sort.FindIndex(entry => entry.Property == property);
            var direction = index >= 0 && _sort[index].Direction == ListSortDirection.Ascending
                ? ListSortDirection.Descending : ListSortDirection.Ascending;
            if (!System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
                _sort.Clear();
            index = _sort.FindIndex(entry => entry.Property == property);
            if (index < 0) _sort.Add((property, direction));
            else _sort[index] = (property, direction);
            Filter();
        };
        _items.ToolTip = T("Shift + щелчок по заголовку добавляет поле сортировки", "Shift + click a header to add a sort field");
        AddColumn(T("Весь запас", "Stock value"), nameof(Item.StockValue), DataGridLength.Auto);
        AddColumn(T("Тип", "Type"), nameof(Item.Type), DataGridLength.Auto);
    }

    private static void DockTop(DockPanel panel, UIElement element)
    {
        DockPanel.SetDock(element, Dock.Top);
        panel.Children.Add(element);
    }

    private static Button Action(string caption, Action callback)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap },
            MinHeight = 29, Margin = new Thickness(3), Padding = new Thickness(8, 4, 8, 4)
        };
        button.Click += (_, _) => callback();
        return button;
    }

    private static Border Surface(UIElement child, string brush, int radius)
    {
        var border = new Border
        {
            Child = child, CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1),
            Padding = new Thickness(8), Margin = new Thickness(3), SnapsToDevicePixels = true
        };
        border.SetResourceReference(Border.BackgroundProperty, brush);
        border.SetResourceReference(Border.BorderBrushProperty, "RoSoftBorderBrush");
        return border;
    }

    private static void Heading(Panel panel, string value)
    {
        var title = new TextBlock
        {
            Text = value, FontFamily = new FontFamily("Georgia"), FontSize = 23,
            FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "RoDeepBlueBrush");
        panel.Children.Add(title);
    }

    private Grid Split(UIElement left, StackPanel right)
    {
        var grid = new Grid { UseLayoutRounding = true };
        var leftColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        var gap = new ColumnDefinition { Width = new GridLength(7) };
        var detail = new ColumnDefinition { Width = new GridLength(300), MinWidth = 180 };
        grid.ColumnDefinitions.Add(leftColumn);
        grid.ColumnDefinitions.Add(gap);
        grid.ColumnDefinitions.Add(detail);
        var main = Surface(left, "RoPanelBrush", 10);
        var card = Surface(new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, "RoPanelBrush", 10);
        var splitter = new GridSplitter { Width = 7, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns };
        splitter.SetResourceReference(BackgroundProperty, "RoSoftBorderBrush");
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(card, 2);
        var narrow = new TabControl { Visibility = Visibility.Collapsed };
        narrow.SetResourceReference(StyleProperty, "SectionTabControlStyle");
        var tableTab = new TabItem { Header = T("Список", "List") };
        var detailTab = new TabItem { Header = T("Карточка", "Details") };
        narrow.Items.Add(tableTab);
        narrow.Items.Add(detailTab);
        Grid.SetColumnSpan(narrow, 3);
        grid.Children.Add(narrow);
        grid.Children.Add(main);
        grid.Children.Add(splitter);
        grid.Children.Add(card);
        void Layout()
        {
            var stacked = grid.ActualWidth is > 0 and < 680;
            if (stacked && narrow.Visibility != Visibility.Visible)
            {
                grid.Children.Remove(main);
                grid.Children.Remove(card);
                tableTab.Content = main;
                detailTab.Content = card;
            }
            else if (!stacked && !grid.Children.Contains(main))
            {
                tableTab.Content = null;
                detailTab.Content = null;
                grid.Children.Add(main);
                grid.Children.Add(card);
            }
            narrow.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
            splitter.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
            gap.Width = new GridLength(stacked ? 0 : 7);
            detail.MinWidth = stacked ? 0 : 180;
            detail.Width = new GridLength(stacked ? 0 : Math.Clamp(Preferences?.StorageDetailWidth ?? 300, 180,
                Math.Max(180, grid.ActualWidth - 300)));
        }
        grid.Loaded += (_, _) => Layout();
        grid.SizeChanged += (_, _) => Layout();
        splitter.DragCompleted += (_, _) =>
        {
            if (Preferences is { } settings) { settings.StorageDetailWidth = detail.ActualWidth; SavePreferences(); }
        };
        return grid;
    }

    private long NpcWithOvercharge(long price)
    {
        int[] rates = [0, 7, 9, 11, 13, 15, 17, 19, 21, 23, 24];
        return (long)Math.Min(long.MaxValue, Math.Floor(price * (100m + rates[Math.Clamp(_overcharge.SelectedIndex, 0, 10)]) / 100m));
    }

    private static ILookup<int, StorageBid> BuildBids(IEnumerable<MarketListing> listings) =>
        listings.Where(row => row.ShopType == "BUY").Select(row => (row.ItemId,
            Price: SpringRoMarketService.ParsePrice(row.Price), Quantity: SpringRoMarketService.ParsePrice(row.Quantity)))
            .Where(row => row.Price is > 0 and < long.MaxValue && row.Quantity is > 0 and < long.MaxValue)
            .OrderByDescending(row => row.Price).ToLookup(row => row.ItemId, row => new StorageBid(row.Price, row.Quantity));

    private decimal Value(Item item, long count, long offset = 0)
    {
        long npc = item.Npc ?? 0;
        if (_valuation.SelectedIndex != 1) return count * (decimal)Math.Max(npc, item.Vending ?? 0);
        decimal total = 0;
        foreach (var bid in item.Bids)
        {
            if (count == 0 || bid.Price <= npc) break;
            var skipped = Math.Min(offset, bid.Quantity);
            offset -= skipped;
            var sold = Math.Min(count, bid.Quantity - skipped);
            total += sold * (decimal)bid.Price;
            count -= sold;
        }
        return total + count * (decimal)npc;
    }

    private void Filter()
    {
        if (_updatingTypes) return;
        var query = _search.Text.Trim();
        var type = (_types.SelectedItem as TabItem)?.Tag as string;
        decimal? min = decimal.TryParse(_minimum.Text, out var low) && low >= 0 ? low : null;
        decimal? max = decimal.TryParse(_maximum.Text, out var high) && high >= 0 ? high : null;
        var candidates = _summary.Where(item =>
        {
            var value = _priceBasis.SelectedIndex == 1 ? item.UnitValue : item.Total;
            return (int.TryParse(query.TrimStart('#'), out var id) ? item.Id == id :
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                Preferences?.StorageItemTags.GetValueOrDefault(item.Id)?.Any(tag => tag.Contains(query, StringComparison.CurrentCultureIgnoreCase)) == true) &&
                (_pinnedOnly.IsChecked != true || Preferences?.StoragePinnedItems.Contains(item.Id) == true) &&
                (_storageSlot is null || item.Entries.Any(entry => entry.Slot == _storageSlot)) &&
                (min is null || value >= min) && (max is null || value <= max) &&
                (_filter.SelectedIndex switch { 1 => !item.Craft && !item.Quest, 2 => item.Craft, 3 => item.Quest, _ => true });
        }).ToArray();
        foreach (var tab in _types.Items.OfType<TabItem>())
        {
            var category = tab.Tag as string;
            tab.Header = $"{category ?? T("Все типы", "All types")} ({candidates.Count(item => category is null || item.Type == category)})";
        }
        var rows = candidates.Where(item => type is null || item.Type == type).ToArray();
        var previous = (_items.SelectedItem as Item)?.Id;
        var view = new ListCollectionView(rows);
        foreach (var sort in _sort) view.SortDescriptions.Add(new SortDescription(sort.Property, sort.Direction));
        _items.ItemsSource = view;
        foreach (var column in _items.Columns)
        {
            var property = column.SortMemberPath;
            if (string.IsNullOrEmpty(property) && column is DataGridBoundColumn { Binding: Binding binding })
                property = binding.Path.Path;
            column.SortDirection = _sort.Where(entry => entry.Property == property).Select(entry => (ListSortDirection?)entry.Direction).FirstOrDefault();
        }
        _items.SelectedItem = rows.FirstOrDefault(item => item.Id == previous);
        _filteredTotals.Text = T($"Выборка · позиций {rows.Length:N0} · единиц {rows.Sum(item => item.Count):N0} · {rows.Sum(item => item.Total):N0} z",
            $"Filtered · {rows.Length:N0} positions · {rows.Sum(item => item.Count):N0} units · {rows.Sum(item => item.Total):N0} z");
        RenderMetrics(rows);
        _chips.Children.Clear();
        if (query.Length > 0) Chip(query, () => _search.Clear());
        if (type is not null) Chip(type, () => _types.SelectedIndex = 0);
        if (_filter.SelectedIndex > 0) Chip((string)_filter.SelectedItem, () => _filter.SelectedIndex = 0);
        if (_pinnedOnly.IsChecked == true) Chip(T("Закреплённые", "Pinned"), () => _pinnedOnly.IsChecked = false);
        if (_storageSlot is { } slot) Chip(T($"Хранилище {slot}", $"Storage {slot}"), () => { _storageSlot = null; Filter(); });
        if (_minimum.Text.Length > 0) Chip($"≥ {_minimum.Text}", () => _minimum.Clear());
        if (_maximum.Text.Length > 0) Chip($"≤ {_maximum.Text}", () => _maximum.Clear());
    }

    private void Chip(string caption, Action clear) => _chips.Children.Add(Action(caption + " ×", clear));

    private void RenderMetrics(Item[] rows)
    {
        _metrics.Children.Clear();
        _metrics.Rows = 2;
        var total = rows.Sum(item => item.Total);
        var top = rows.OrderByDescending(item => item.Total).Take(10).Sum(item => item.Total);
        var most = rows.MaxBy(item => item.Count);
        var richest = rows.Where(item => item.Total > 0).MaxBy(item => item.Total);
        Metric(T("Позиции", "Positions"), $"{rows.Length:N0}", nameof(Item.Name));
        Metric(T("Единицы", "Units"), $"{rows.Sum(item => item.Count):N0}", nameof(Item.Count));
        Metric(T("Многочисленный", "Most numerous"), most is null ? "—" : $"{most.Name} · {most.Count:N0}", nameof(Item.Count), most);
        Metric(T("Стоимость", "Value"), $"{total:N0} z", nameof(Item.Total));
        Metric(T("Дорогой", "Highest value"), richest is null ? "—" : $"{richest.Name} · {richest.Total:N0} z", nameof(Item.Total), richest);
        Metric(T("Доля топ-10", "Top 10 share"), total > 0 ? $"{top / total:P1}" : "—", nameof(Item.Total));
        var slots = new decimal[3];
        foreach (var item in rows)
        {
            long offset = 0;
            foreach (var entry in item.Entries.OrderBy(entry => entry.Slot))
            {
                slots[entry.Slot - 1] += Value(item, entry.Amount, offset);
                offset += entry.Amount;
            }
        }
        for (var slot = 1; slot <= slots.Length; slot++)
        {
            var number = slot;
            Metric(T($"Хранилище {slot}", $"Storage {slot}"), $"{slots[slot - 1]:N0} z", nameof(Item.Total),
                beforeSort: () => _storageSlot = _storageSlot == number ? null : number);
        }
    }

    private void Metric(string title, string value, string property, Item? target = null, Action? beforeSort = null)
    {
        var button = Action($"{title}\n{value}", () =>
        {
            beforeSort?.Invoke();
            _sort.Clear();
            _sort.Add((property, property == nameof(Item.Name) ? ListSortDirection.Ascending : ListSortDirection.Descending));
            Filter();
            _items.SelectedItem = target is null ? _items.Items.Cast<Item>().FirstOrDefault() :
                _items.Items.Cast<Item>().FirstOrDefault(item => item.Id == target.Id);
            if (_items.SelectedItem is { } selection) _items.ScrollIntoView(selection);
        });
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.SetResourceReference(BackgroundProperty, "RoTitleBrush");
        _metrics.Children.Add(button);
    }

    private void ApplyTag(bool add)
    {
        if (Preferences is not { } settings || _tag.Text.Trim() is not { Length: > 0 } label) return;
        foreach (var item in _items.SelectedItems.OfType<Item>())
        {
            var tags = new HashSet<string>(settings.StorageItemTags.GetValueOrDefault(item.Id) ?? [], StringComparer.CurrentCultureIgnoreCase);
            if (add) tags.Add(label);
            else tags.Remove(label);
            if (tags.Count == 0) settings.StorageItemTags.Remove(item.Id);
            else settings.StorageItemTags[item.Id] = tags.Order().ToArray();
        }
        SavePreferences();
        Filter();
        ShowItem(_items.SelectedItem as Item);
    }

    private void ShowItemExtras(Item item)
    {
        Text(_details, $"#{item.Id} · {item.Type}");
        if (Preferences is { } settings)
        {
            var pinned = settings.StoragePinnedItems.Contains(item.Id);
            _details.Children.Add(Action(pinned ? T("Открепить", "Unpin") : T("Закрепить", "Pin"), () =>
            {
                if (pinned) settings.StoragePinnedItems.Remove(item.Id);
                else settings.StoragePinnedItems.Add(item.Id);
                SavePreferences();
                Filter();
                ShowItem(_items.SelectedItem as Item);
            }));
            if (settings.StorageItemTags.GetValueOrDefault(item.Id) is { Length: > 0 } tags)
                Text(_details, string.Join(" · ", tags));
        }
        Text(_details, T($"Оценка запаса · {item.Total:N0} z", $"Stock value · {item.Total:N0} z"), true);
        Text(_details, item.Craft ? T("Крафт: используется", "Craft: used") : T("Крафт: связей нет", "Craft: no links"));
        Text(_details, item.Quest ? T("Задания: используется", "Quests: used") : T("Задания: связей нет", "Quests: no links"));
        ShowHistory(item);
    }
}
