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
    private Button _login = null!;
    private readonly TextBlock _overview = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
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
        _login = Action(T("Войти в Ragnarok+", "Sign in to Ragnarok+"), async () =>
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
        _login.Visibility = Visibility.Collapsed;
        toolbar.Children.Add(_login);
        toolbar.Children.Add(_lastImport);
        _load.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        _load.Foreground = Brushes.White;
    }

    private void ConfigureFilters(DockPanel analysis)
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
        foreach (var control in new Control[] { _priceBasis, _minimum, _maximum, _valuation, _overcharge, _pinnedOnly })
            control.Margin = new Thickness(4);
        var overview = new DockPanel();
        var expand = Action(T("Подробнее ▾", "Summary ▾"), () =>
            _overviewDetails.Visibility = _overviewDetails.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
        DockPanel.SetDock(expand, Dock.Right);
        overview.Children.Add(expand);
        overview.Children.Add(_overview);
        DockTop(analysis, Surface(overview, "RoTitleBrush", 6));
        _overviewDetails.Children.Add(_metrics);
        var pricing = new WrapPanel();
        pricing.Children.Add(_valuation);
        pricing.Children.Add(_overcharge);
        _overviewDetails.Children.Add(pricing);
        DockTop(analysis, _overviewDetails);

        var filters = new StackPanel { Width = 260 };
        filters.Children.Add(_filter);
        filters.Children.Add(_priceBasis);
        var amount = new WrapPanel();
        amount.Children.Add(new TextBlock { Text = T("От", "From"), VerticalAlignment = VerticalAlignment.Center });
        amount.Children.Add(_minimum);
        amount.Children.Add(new TextBlock { Text = T("До", "To"), VerticalAlignment = VerticalAlignment.Center });
        amount.Children.Add(_maximum);
        filters.Children.Add(amount);
        var tools = new DockPanel { Margin = new Thickness(0, 2, 0, 7) };
        var actions = new WrapPanel();
        actions.Children.Add(_types);
        actions.Children.Add(PopupAction(T("Фильтры ▾", "Filters ▾"), filters));
        actions.Children.Add(_pinnedOnly);
        DockPanel.SetDock(actions, Dock.Right);
        tools.Children.Add(actions);
        tools.Children.Add(_search);
        AlignFields(_search, _types, _pinnedOnly);
        foreach (var button in actions.Children.OfType<Button>()) AlignFields(button);
        DockTop(analysis, tools);
        DockTop(analysis, _chips);

        var status = Surface(_filteredTotals, "RoTitleBrush", 6);
        DockPanel.SetDock(status, Dock.Bottom);
        analysis.Children.Add(status);
        _types.Items.Add(new ComboBoxItem { Content = T("Все типы", "All types") });
        _types.SelectedIndex = 0;
        _overview.Text = T("Обновите хранилище, чтобы увидеть остатки.", "Refresh storage to see your stock.");
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
    }

    private readonly StackPanel _overviewDetails = new() { Visibility = Visibility.Collapsed };

    private static Button PopupAction(string caption, UIElement body, string? hint = null)
    {
        var button = new Button { Content = caption, ToolTip = hint, Margin = new Thickness(3) };
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true, Child = Surface(body, "RoPanelBrush", 8)
        };
        button.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        return button;
    }

    private void AddItemColumn()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var icon = new FrameworkElementFactory(typeof(Image));
        icon.SetValue(FrameworkElement.WidthProperty, 18d);
        icon.SetValue(FrameworkElement.HeightProperty, 18d);
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        icon.SetBinding(Image.SourceProperty, new Binding(nameof(Item.Id)) { Mode = BindingMode.OneWay, Converter = new ItemIcon(this) });
        icon.SetValue(DockPanel.DockProperty, Dock.Left);
        row.AppendChild(icon);
        var box = new FrameworkElementFactory(typeof(TextBlock));
        box.SetBinding(TextBlock.TextProperty, new Binding(nameof(Item.Name)) { Mode = BindingMode.OneWay });
        box.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var badges = new FrameworkElementFactory(typeof(StackPanel));
        badges.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        badges.SetValue(DockPanel.DockProperty, Dock.Right);
        badges.AppendChild(ItemBadge("craft", nameof(Item.Craft), "#A65310"));
        badges.AppendChild(ItemBadge("quest", nameof(Item.Quest), "#257449"));
        row.AppendChild(badges);
        row.AppendChild(box);
        _items.Columns.Add(new DataGridTemplateColumn
        {
            Header = T("Предмет", "Item"), SortMemberPath = nameof(Item.Name),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            CellTemplate = new DataTemplate { VisualTree = row }
        });
    }

    private readonly Dictionary<int, ImageSource?> _itemIcons = [];

    private ImageSource? GetItemIcon(int id)
    {
        if (_itemIcons.TryGetValue(id, out var source)) return source;
        if (_database?.Item(id)?.LocalImagePng is not { Length: > 0 } bytes) return null;
        source = new RoAura.Converters.TimerImageConverter().Convert(bytes, typeof(ImageSource), "", CultureInfo.InvariantCulture) as ImageSource;
        if (source?.CanFreeze == true) source.Freeze();
        return _itemIcons[id] = source;
    }

    private sealed class ItemIcon(StorageAnalysisControl owner) : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int id ? owner.GetItemIcon(id) : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private static FrameworkElementFactory ItemBadge(string label, string property, string color)
    {
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
        badge.SetValue(Border.BorderBrushProperty, Brushes.White);
        badge.SetValue(Border.BorderThicknessProperty, new Thickness(.5));
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        badge.SetValue(Border.PaddingProperty, new Thickness(3, 0, 3, 0));
        badge.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 0, 0));
        badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        badge.SetBinding(VisibilityProperty, new Binding(property) { Mode = BindingMode.OneWay, Converter = new BooleanToVisibilityConverter() });
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, label);
        text.SetValue(TextBlock.FontSizeProperty, 10d);
        text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        badge.AppendChild(text);
        return badge;
    }

    private void ConfigureGrid()
    {
        _items.RowHeight = 24;
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
        _items.HeadersVisibility = DataGridHeadersVisibility.Column;
    }

    private static void AlignFields(params Control[] fields)
    {
        foreach (var field in fields)
        {
            field.Height = 30;
            field.Margin = new Thickness(3, 0, 3, 0);
            field.VerticalContentAlignment = VerticalAlignment.Center;
        }
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

    private Grid Split(UIElement left, StackPanel right, System.Windows.Controls.Primitives.Selector? selector = null)
    {
        var grid = new Grid { UseLayoutRounding = true };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
        grid.ColumnDefinitions[0].MinWidth = 0;
        var detail = new ColumnDefinition { Width = new GridLength(330) };
        grid.ColumnDefinitions.Add(detail);
        var main = Surface(left, "RoPanelBrush", 10);
        var details = new DockPanel();
        var back = new Button { Content = T("‹ К списку", "‹ Back to list"), HorizontalAlignment = HorizontalAlignment.Left };
        DockTop(details, back);
        var scroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6) };
        details.Children.Add(scroll);
        var card = Surface(details, "RoPanelBrush", 10);
        var splitter = new GridSplitter { Width = 7, ResizeBehavior = GridResizeBehavior.PreviousAndNext, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns };
        splitter.SetResourceReference(BackgroundProperty, "RoSoftBorderBrush");
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(main);
        grid.Children.Add(splitter);
        grid.Children.Add(card);
        bool selected = false;
        double preferredWidth = 330;
        void Layout()
        {
            var narrow = grid.ActualWidth is > 0 and < 760;
            main.Visibility = narrow && selected ? Visibility.Hidden : Visibility.Visible;
            card.Visibility = narrow && !selected ? Visibility.Collapsed : Visibility.Visible;
            splitter.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            back.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(card, narrow ? 0 : 2);
            grid.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 7);
            grid.ColumnDefinitions[0].MinWidth = narrow ? 0 : 400;
            detail.MinWidth = narrow ? 0 : 300;
            detail.Width = new GridLength(narrow ? 0 : Math.Clamp(Preferences?.StorageDetailWidth ?? preferredWidth, 300,
                Math.Max(300, grid.ActualWidth - 407)));
        }
        void Open()
        {
            selected = true;
            scroll.ScrollToTop();
            Layout();
        }
        back.Click += (_, _) => { selected = false; Layout(); left.Focus(); };
        if (selector is not null)
        {
            selector.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject origin &&
                    ItemsControl.ContainerFromElement(selector, origin) is DataGridRow) Open();
            };
            selector.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter && selector.SelectedItem is not null) { Open(); e.Handled = true; }
            };
        }
        else if (left is DockPanel && right == _craftDetails)
        {
            _craft.SelectedItemChanged += (_, e) => { if (e.NewValue is TreeViewItem { Tag: StorageAnalysis.Craft }) scroll.ScrollToTop(); };
            _craft.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject origin &&
                    ItemsControl.ContainerFromElement(null, origin) is TreeViewItem { Tag: StorageAnalysis.Craft }) Open();
            };
            _craft.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter && _craft.SelectedItem is TreeViewItem { Tag: StorageAnalysis.Craft })
                { Open(); e.Handled = true; }
            };
        }
        grid.Loaded += (_, _) => Layout();
        grid.SizeChanged += (_, _) => Layout();
        splitter.DragCompleted += (_, _) =>
        {
            preferredWidth = detail.ActualWidth;
            if (Preferences is { } settings) { settings.StorageDetailWidth = preferredWidth; SavePreferences(); }
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
        var type = (_types.SelectedItem as ComboBoxItem)?.Tag as string;
        decimal? min = decimal.TryParse(_minimum.Text, out var low) && low >= 0 ? low : null;
        decimal? max = decimal.TryParse(_maximum.Text, out var high) && high >= 0 ? high : null;
        var candidates = _summary.Where(item =>
        {
            var value = _priceBasis.SelectedIndex == 1 ? item.UnitValue : item.Total;
            return (int.TryParse(query.TrimStart('#'), out var id) ? item.Id == id :
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
                (_pinnedOnly.IsChecked != true || Preferences?.StoragePinnedItems.Contains(item.Id) == true) &&
                (_storageSlot is null || item.Entries.Any(entry => entry.Slot == _storageSlot)) &&
                (min is null || value >= min) && (max is null || value <= max) &&
                (_filter.SelectedIndex switch { 1 => !item.Craft && !item.Quest, 2 => item.Craft, 3 => item.Quest, _ => true });
        }).ToArray();
        foreach (var tab in _types.Items.OfType<ComboBoxItem>())
        {
            var category = tab.Tag as string;
            tab.Content = $"{category ?? T("Все типы", "All types")} ({candidates.Count(item => category is null || item.Type == category)})";
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
        RenderMetrics(_summary.Where(item => type is null || item.Type == type).ToArray());
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
        _metrics.Columns = 3;
        var total = rows.Sum(item => item.Total);
        var top = rows.OrderByDescending(item => item.Total).Take(10).Sum(item => item.Total);
        var most = rows.MaxBy(item => item.Count);
        var richest = rows.Where(item => item.Total > 0).MaxBy(item => item.Total);
        var units = rows.Sum(item => item.Count);
        _overview.Text = T($"{rows.Length:N0} позиций  ·  {units:N0} единиц  ·  {total:N0} z",
            $"{rows.Length:N0} positions  ·  {units:N0} units  ·  {total:N0} z");
        Metric(T("Больше всего, шт.", "Most units"), most is null ? "—" : $"{most.Name} · {most.Count:N0}", nameof(Item.Count), most);
        Metric(T("Наибольшая стоимость", "Highest value"), richest is null ? "—" : $"{richest.Name} · {richest.Total:N0} z", nameof(Item.Total), richest);
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
        var button = Action(title, () =>
        {
            beforeSort?.Invoke();
            _sort.Clear();
            _sort.Add((property, property == nameof(Item.Name) ? ListSortDirection.Ascending : ListSortDirection.Descending));
            Filter();
            _items.SelectedItem = target is null ? _items.Items.Cast<Item>().FirstOrDefault() :
                _items.Items.Cast<Item>().FirstOrDefault(item => item.Id == target.Id);
            if (_items.SelectedItem is { } selection) _items.ScrollIntoView(selection);
        });
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = value, FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        button.Content = content;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.SetResourceReference(BackgroundProperty, "RoTitleBrush");
        _metrics.Children.Add(button);
    }

    private void ShowItemExtras(Item item)
    {
        var header = new DockPanel();
        AddCardIcon(header, item.Id);
        var pinned = Preferences?.StoragePinnedItems.Contains(item.Id) == true;
        var pin = Action(pinned ? "★" : "☆", () =>
        {
            if (Preferences is not { } settings) return;
            if (pinned) settings.StoragePinnedItems.Remove(item.Id);
            else settings.StoragePinnedItems.Add(item.Id);
            SavePreferences();
            Filter();
            ShowItem(_items.SelectedItem as Item);
        });
        pin.Width = pin.Height = 30;
        pin.Padding = new Thickness(0);
        pin.VerticalAlignment = VerticalAlignment.Top;
        pin.ToolTip = pinned ? T("Открепить", "Unpin") : T("Закрепить", "Pin");
        DockPanel.SetDock(pin, Dock.Right);
        header.Children.Add(pin);
        var title = new StackPanel();
        Heading(title, item.Name);
        Text(title, $"#{item.Id} · {item.Type}");
        header.Children.Add(title);
        _details.Children.Add(header);
        Link(_details, T("Открыть в базе ↗", "Open in database ↗"), item.Id);
        Tiles(_details, (T("Остаток", "Stock"), $"{item.Count:N0}"),
            (T("Оценка запаса", "Stock value"), $"{item.Total:N0} z"),
            (T("За единицу", "Per unit"), $"{item.UnitValue:N0} z"));
    }

    private void AddCardIcon(DockPanel header, int id)
    {
        var source = GetItemIcon(id);
        UIElement icon = source is null ? new TextBlock { Text = "◇", FontSize = 24, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center } : new Image { Source = source, Stretch = Stretch.Uniform };
        var frame = Surface(icon, "RoTitleBrush", 4);
        frame.Width = frame.Height = 46;
        frame.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(frame, Dock.Left);
        header.Children.Add(frame);
    }

    private static void Section(Panel panel, string label)
    {
        var border = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 0) };
        border.SetResourceReference(Border.BorderBrushProperty, "RoSoftBorderBrush");
        panel.Children.Add(border);
        Text(panel, label, true);
    }

    private static void Tiles(Panel panel, params (string Caption, string Value)[] metrics)
    {
        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        foreach (var (caption, value) in metrics)
        {
            var tile = new StackPanel();
            Text(tile, caption);
            Text(tile, value, true);
            tiles.Children.Add(Surface(tile, "RoTitleBrush", 6));
        }
        panel.Children.Add(tiles);
    }
}
