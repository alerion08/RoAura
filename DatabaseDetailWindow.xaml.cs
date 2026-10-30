using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseDetailWindow : Window
{
    public event Action? HideRequested;
    private readonly AppSettings _settings;
    private readonly GameDatabaseService _database;
    private readonly SpringRoItemDetailService _itemDetails = new();
    private int _detailVersion;
    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public DatabaseDetailWindow(AppSettings settings, GameDatabaseService database)
    {
        InitializeComponent();
        _settings = settings;
        _database = database;
        RefreshLanguage();
    }

    public void RefreshLanguage()
    {
        PanelTitle.Text = T("Результат", "Result");
        KeyboardStatus.Text = T($"Ввод в поиск активен · {_settings.DatabaseHotkey} — закрыть",
            $"Search input active · {_settings.DatabaseHotkey} — close");
        CloseButton.ToolTip = T("Закрыть базу данных", "Close database");
    }

    public void PlaceForGame(ForegroundProcess.WindowBounds game, double width, double height, double margin)
    {
        Width = width;
        Height = height;
        Left = game.Right - width - margin;
        Top = game.Top + ((game.Bottom - game.Top) - height) / 2;
    }

    public void ResetServer() { _detailVersion++; Clear(); }

    public void Clear() => DetailsPanel.Children.Clear();

    public async void ShowDetails(object value)
    {
        var version = ++_detailVersion;
        Clear();
        switch (value)
        {
            case DatabaseMonster monster: ShowMonster(monster); break;
            case DatabaseItem item when _database.Version == GameDatabaseVersion.Toc:
                ShowItem(item, new SpringRoItemDetails { ImageUrl = item.ImageUrl, Description = item.Description,
                    NpcSellValue = item.NpcSellPrice.ToString("N0") }, false);
                break;
            case DatabaseItem item:
                ShowItem(item, null, true);
                try
                {
                    var detail = await _itemDetails.GetAsync(item.Id);
                    if (version != _detailVersion) return;
                    Clear();
                    ShowItem(item, detail, false);
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"ITEM DETAILS LOAD ERROR #{item.Id}: {ex.Message}");
                    if (version != _detailVersion) return;
                    Clear();
                    ShowItem(item, null, false);
                }
                break;
            case DatabaseMap map: ShowMap(map); break;
            case MarketListing listing: ShowMarketListing(listing); break;
        }
    }

    private void ShowMonster(DatabaseMonster monster)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (Uri.TryCreate(monster.ImageUrl, UriKind.Absolute, out var uri))
            header.Children.Add(new Image { Source = new BitmapImage(uri), Width = 58, Height = 58, Stretch = Stretch.Uniform, Margin = new Thickness(0,0,10,0) });
        var title = new StackPanel(); title.Children.Add(Heading(monster.Name, 20));
        title.Children.Add(Meta($"#{monster.Id} · Lv {monster.Level}\n{monster.Race} · {monster.Element} · {monster.Size}"));
        header.Children.Add(title); DetailsPanel.Children.Add(header);
        ShowMonsterStatistics(monster);
        ShowMonsterSkills(monster.Skills);
        AddSection(T($"Дроп ({monster.Drops.Count})", $"Drops ({monster.Drops.Count})"));
        foreach (var drop in monster.Drops.OrderByDescending(x => ParseRate(x.Rate)))
            AddLink($"{drop.Name} — {drop.Rate}{(drop.IsMvp ? " MVP" : "")}{(_database.Version == GameDatabaseVersion.Toc && drop.IsMvp ? T(" · базовый шанс", " · base rate") : "")}", () => OpenItem(drop.ItemId));
        AddSection(T($"Локации ({monster.Spawns.Count})", $"Maps ({monster.Spawns.Count})"));
        foreach (var spawn in monster.Spawns.OrderByDescending(x => ParseCount(x.Count)))
            AddLink($"{spawn.Name}\n{spawn.Count} · {spawn.Respawn}", () => OpenMap(spawn.Code));
    }

    private void ShowItem(DatabaseItem item, SpringRoItemDetails? detail, bool loading)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var imageUrl = detail?.ImageUrl;
        if (string.IsNullOrWhiteSpace(imageUrl)) imageUrl = item.ImageUrl;
        if (string.IsNullOrWhiteSpace(imageUrl))
            imageUrl = $"https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/items/{item.Id}.png";
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
            header.Children.Add(new Image { Source = new BitmapImage(imageUri), Width = 56, Height = 56,
                Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) });
        var title = new StackPanel();
        var itemHeading = Heading(item.Name, 20); itemHeading.MaxWidth = 180;
        title.Children.Add(itemHeading);
        title.Children.Add(Meta($"Item ID #{item.Id} · {item.Type}"));
        header.Children.Add(title);
        var itemHeader = new DockPanel();
        var star = new Controls.MarketWatchStar { Item = item };
        DockPanel.SetDock(star, Dock.Right);
        itemHeader.Children.Add(star);
        itemHeader.Children.Add(header);
        DetailsPanel.Children.Add(itemHeader);

        AddSection(T("Информация", "Information"));
        if (_database.Version == GameDatabaseVersion.Toc) ShowTocItemInformation(item);
        else if (loading)
            DetailsPanel.Children.Add(Meta(T("Загружаем актуальные данные SpringRO…", "Loading current SpringRO data…")));
        else if (detail is not null)
        {
            AddLine(T("Цена продажи NPC", "NPC sell value"), detail.NpcSellValue.Length > 0 ? detail.NpcSellValue : "—");
            if (_database.Version != GameDatabaseVersion.Toc)
                AddLine(T("Количество на сервере", "Copies on server"), detail.ServerCopies.Length > 0 ? detail.ServerCopies : "—");
            if (!string.IsNullOrWhiteSpace(detail.Description))
            {
                AddSection(T("Описание и эффекты", "Description & effects"));
                DetailsPanel.Children.Add(new TextBlock { Text = detail.Description, TextWrapping = TextWrapping.Wrap,
                    FontSize = 12, LineHeight = 18, Margin = new Thickness(0, 1, 0, 3) });
            }
        }
        else DetailsPanel.Children.Add(Meta(T("Актуальные данные временно недоступны.", "Current data is temporarily unavailable.")));
        AddSection(T($"Источники ({item.DroppedBy.Count})", $"Sources ({item.DroppedBy.Count})"));
        if (item.DroppedBy.Count == 0) DetailsPanel.Children.Add(Meta(T($"Нет в дроп-листах монстров {_database.Version}.", $"Not found in {_database.Version} monster drop lists.")));
        foreach (var source in item.DroppedBy.OrderByDescending(x => ParseRate(x.Rate)))
            AddLink($"{source.Name} — {source.Rate}{(source.IsMvp ? " MVP" : "")}{(_database.Version == GameDatabaseVersion.Toc && source.IsMvp ? T(" · базовый шанс", " · base rate") : "")}", () => OpenMonster(source.MonsterId));
    }

    private void ShowMap(DatabaseMap map)
    {
        var density = _database.GetMapExperienceDensity(map);
        DetailsPanel.Children.Add(Heading(map.Name, 20));
        DetailsPanel.Children.Add(Meta(map.Code));
        AddSection(T("Плотность опыта", "Experience density"));
        AddLine(T("Монстров на карте", "Monsters on map"), $"{density.MonsterCount:N0}");
        AddLine(T("Средний Base EXP за моба", "Average Base EXP per mob"), $"{density.BaseExperience:N0}");
        AddLine(T("Средний Job EXP за моба", "Average Job EXP per mob"), $"{density.JobExperience:N0}");
        AddSection(T($"Монстры ({map.Monsters.Count})", $"Monsters ({map.Monsters.Count})"));
        foreach (var mob in map.Monsters.OrderByDescending(x => ParseCount(x.Count)))
            AddLink($"{mob.Name} · {mob.Count} · {mob.Respawn}", () => OpenMonster(mob.MonsterId));
    }

    private void ShowMarketListing(MarketListing listing)
    {
        DetailsPanel.Children.Add(Heading(listing.ItemName, 20));
        DetailsPanel.Children.Add(Meta($"Item ID #{listing.ItemId} · {(listing.ShopType == "BUY" ? T("Покупка", "Buying") : T("Продажа", "Selling"))}"));
        AddSection(T("Предложение", "Listing"));
        AddLine(T("Цена", "Price"), listing.Price);
        AddLine(T("Количество", "Quantity"), listing.Quantity);
        AddLine(T("Заточка", "Refine"), listing.Refine);
        AddLine(T("Карты", "Cards"), listing.Cards);
        AddLine(T("Магазин", "Stall"), listing.Stall);
        AddLine(T("Владелец", "Owner"), listing.Owner);
        AddSection(T("Координаты магазина", "Shop location"));
        AddLine(T("Локация", "Location"), $"{listing.Map} ({listing.X},{listing.Y})");
        AddMarketMap(listing);
        var navi = new Button
        {
            Content = $"Navi · {listing.NavigationCommand}",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 7, 0, 2), Padding = new Thickness(8, 6, 8, 6)
        };
        navi.Click += (_, _) =>
        {
            navi.Content = TryCopyToClipboard(listing.NavigationCommand)
                ? T("Скопировано в буфер обмена", "Copied to clipboard")
                : T("Не удалось скопировать", "Could not copy");
        };
        DetailsPanel.Children.Add(navi);
    }

    private void AddMarketMap(MarketListing listing)
    {
        const double size = 238;
        var canvas = new Canvas { Width = size, Height = size, Background = Brushes.Black };
        var mapUrl = $"https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/minimaps/{listing.Map}.png";
        if (Uri.TryCreate(mapUrl, UriKind.Absolute, out var uri))
            canvas.Children.Add(new Image { Source = new BitmapImage(uri), Width = size, Height = size, Stretch = Stretch.Uniform });

        if (listing.MapWidth > 0 && listing.MapHeight > 0)
        {
            var maximum = Math.Max(listing.MapWidth, listing.MapHeight);
            var left = (listing.X + (maximum - listing.MapWidth) / 2d) / maximum * size;
            var top = ((maximum - listing.MapHeight) / 2d + listing.MapHeight - listing.Y) / maximum * size;
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = 15, Height = 15, Fill = Brushes.Red, Stroke = Brushes.White, StrokeThickness = 3
            };
            Canvas.SetLeft(ring, left - ring.Width / 2);
            Canvas.SetTop(ring, top - ring.Height / 2);
            Panel.SetZIndex(ring, 2);
            canvas.Children.Add(ring);
        }

        DetailsPanel.Children.Add(new Border
        {
            Child = canvas, BorderBrush = new SolidColorBrush(Color.FromRgb(169, 192, 221)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(3), Margin = new Thickness(0, 7, 0, 5), HorizontalAlignment = HorizontalAlignment.Center
        });
    }

    private void OpenMonster(int id) { var value = _database.Monster(id); if (value is not null) ShowDetails(value); }
    private void OpenItem(int id) { var value = _database.Item(id); if (value is not null) ShowDetails(value); }
    private void OpenMap(string code) { var value = _database.Map(code); if (value is not null) ShowDetails(value); }
    private void AddSection(string text) => DetailsPanel.Children.Add(new TextBlock { Text = text, FontFamily = new FontFamily("Georgia"), FontWeight = FontWeights.Bold, FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(23,78,145)), Margin = new Thickness(0,15,0,5) });
    private void AddLine(string name, string value) => DetailsPanel.Children.Add(new TextBlock { Text = $"{name}: {value}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,2,0,2), FontSize = 12 });
    private void AddLink(string text, Action action) { var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }; var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,2,0,2), Padding = new Thickness(7,4,7,4), FontSize = 11 }; button.Click += (_, _) => action(); DetailsPanel.Children.Add(button); }
    private static TextBlock Heading(string text, double size) => new() { Text = text, FontFamily = new FontFamily("Georgia"), FontWeight = FontWeights.Bold, FontSize = size, Foreground = new SolidColorBrush(Color.FromRgb(23,78,145)), TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
    private static TextBlock Meta(string text) => new() { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(91,116,150)), Margin = new Thickness(0,4,0,4), TextWrapping = TextWrapping.Wrap };
    private static double ParseRate(string value) => double.TryParse(value.TrimEnd('%'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    private static int ParseCount(string value) => int.TryParse(value, out var n) ? n : 0;
    private string MonsterField(string value)
    {
        if (_settings.Language == "en") return value;
        return value switch
        {
            "Level" => "Уровень", "Race" => "Раса", "Property" => "Элемент", "Size" => "Размер",
            "Attack" => "Атака", "Defense" => "Защита", "Magic Defense" => "Магическая защита",
            "Monster Hit" => "HIT монстра", "Hit for 100% accuracy" => "HIT для 100% попадания",
            "Flee for 95% dodge" => "FLEE для 95% уклонения", "Base EXP / HP" => "Base EXP / HP",
            "Job EXP / HP" => "Job EXP / HP", "Attack Range" => "Дальность атаки",
            "Skill Range" => "Дальность навыков", "Chase Range" => "Дальность преследования",
            "Walk Interval" => "Скорость движения", "Attack Delay" => "Задержка атаки",
            "Attack Motion" => "Анимация атаки", "Damage Motion" => "Анимация получения урона",
            "Disposition" => "Тип поведения", "Assist" => "Помощь союзникам",
            "Cast Sensor (Idle)" => "Реакция на каст в покое", "Cast Sensor (Chase)" => "Реакция на каст в погоне",
            "Neutral" => "Нейтральный", "Water" => "Вода", "Earth" => "Земля", "Fire" => "Огонь",
            "Wind" => "Ветер", "Poison" => "Яд", "Holy" => "Святость", "Shadow" => "Тьма",
            "Ghost" => "Призрак", "Undead" => "Нежить", _ => value
        };
    }
    private string MonsterValue(string value)
    {
        if (_settings.Language == "en") return value;
        return value switch { "Yes" => "Да", "No" => "Нет", "Aggressive" => "Агрессивный", "Passive" => "Пассивный", _ => value };
    }
    private string T(string ru, string en) => _settings.Language == "en" ? en : ru;

    // The clipboard is a shared COM resource; another application (or the
    // game client) may hold it for a moment and SetText throws COMException.
    private static bool TryCopyToClipboard(string text) => ClipboardService.TryCopy(text);
    private void Close_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke();
}
