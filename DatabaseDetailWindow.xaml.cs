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
    private Action? _appearanceRedraw;
    internal string ItemSearchText { get; set; } = "";

    private TextBlock ItemDescription(string description)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12,
            LineHeight = 18, Margin = new Thickness(0, 1, 0, 3) };
        var query = ItemSearchText.Trim();
        var start = 0;
        while (query.Length > 0 && start < description.Length)
        {
            var match = description.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (match < 0) break;
            text.Inlines.Add(description[start..match]);
            text.Inlines.Add(new System.Windows.Documents.Run(description.Substring(match, query.Length))
                { Background = Brushes.Khaki, Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold });
            start = match + query.Length;
        }
        text.Inlines.Add(description[start..]);
        return text;
    }
    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public DatabaseDetailWindow(AppSettings settings, GameDatabaseService database)
    {
        InitializeComponent();
        _settings = settings;
        _database = database;
        RefreshLanguage();
        Loaded+=(_,_)=>{AppearanceService.Changed-=AppearanceChanged;AppearanceService.Changed+=AppearanceChanged;AppearanceChanged();};
        Closed+=(_,_)=>AppearanceService.Changed-=AppearanceChanged;
    }

    public void RefreshLanguage()
    {
        PanelTitle.Text = T("Результат", "Result");
        KeyboardStatus.Text = T($"Ввод в поиск активен · {_settings.DatabaseHotkey} — закрыть",
            $"Search input active · {_settings.DatabaseHotkey} — close");
        BackButton.Content = T("Назад", "Back");
        CloseButton.ToolTip = T("Закрыть базу данных", "Close database");
    }

    public void PlaceForGame(ForegroundProcess.WindowBounds game, double width, double height, double margin)
    {
        Width = width;
        Height = height;
        Left = game.Right - width - margin;
        Top = game.Top + ((game.Bottom - game.Top) - height) / 2;
    }

    public void ResetServer() => ResetDetails();

    public void Clear() {_appearanceRedraw=null;DetailsPanel.Children.Clear();}
    private void AppearanceChanged()=>_appearanceRedraw?.Invoke();

    private readonly Stack<(Action Page, double Offset)> _back = new();
    private Action? _currentPage;

    internal void ResetDetails()
    {
        ++_detailVersion;
        _back.Clear();
        _currentPage = null;
        BackButton.Visibility = Visibility.Collapsed;
        Clear();
    }

    private void Navigate(Action page)
    {
        if (_currentPage is { } previous)
            _back.Push((previous, DetailsScroll.VerticalOffset));
        _currentPage = page;
    }

    private void RestoreScroll(double offset, int version)
    {
        _returnScroll = (version, offset);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (_detailVersion == version) DetailsScroll.ScrollToVerticalOffset(offset);
        });
    }

    private (int Version, double Offset) _returnScroll;

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (!_back.TryPop(out var previous)) return;
        _currentPage = previous.Page;
        previous.Page();
        RestoreScroll(previous.Offset, _detailVersion);
    }

    private void RefreshBackButton() =>
        BackButton.Visibility = _back.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private sealed class ItemCardView
    {
        public int SourceCount { get; set; } = 10;
        public bool RecipesExpanded { get; set; }
        public string WeightQuantity { get; set; } = "1";
    }

    private ItemCardView? _itemCardView;

    public void ShowDetails(object value) => ShowDetails(value, navigate: true, new ItemCardView());

    private async void ShowDetails(object value, bool navigate, ItemCardView view)
    {
        if (navigate)
        {
            RememberCard(value);
            Navigate(() => ShowDetails(value, false, view));
        }
        _itemCardView = view;
        var version = ++_detailVersion;
        Clear();
        RefreshBackButton();
        DetailsScroll.ScrollToTop();
        switch (value)
        {
            case DatabaseMonster monster: ShowMonster(monster); break;
            case DatabaseItem item when _database.Version is GameDatabaseVersion.Toc or GameServerProfile.RagnarokPlus:
                ShowItem(item, new SpringRoItemDetails { ImageUrl = item.ImageUrl, Description = item.Description,
                    NpcSellValue = _database.Version == GameServerProfile.RagnarokPlus &&
                        !item.RagnarokPlusDetailsCaptured && item.NpcSellPrice == 0 ? "" : item.NpcSellPrice.ToString("N0") }, false);
                break;
            case DatabaseItem item:
                ShowItem(item, null, true);
                try
                {
                    var detail = await _itemDetails.GetAsync(item.Id);
                    if (version != _detailVersion) return;
                    Clear();
                    RefreshBackButton();
                    ShowItem(item, detail, false);
                    if (_returnScroll.Version == version) RestoreScroll(_returnScroll.Offset, version);
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"ITEM DETAILS LOAD ERROR #{item.Id}: {ex.Message}");
                    if (version != _detailVersion) return;
                    Clear();
                    RefreshBackButton();
                    ShowItem(item, null, false);
                    if (_returnScroll.Version == version) RestoreScroll(_returnScroll.Offset, version);
                }
                break;
            case DatabaseMap map: ShowMap(map); break;
            case MarketListing listing: ShowMarketListing(listing); break;
        }
    }

    private void ShowMonster(DatabaseMonster monster)
    {
        _appearanceRedraw=()=>{DetailsPanel.Children.Clear();ShowMonster(monster);};
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (monster.LocalImagePng is { Length: > 0 } sprite)
            header.Children.Add(new Image { Source = DecodeCatalogImage(sprite), Width = 58, Height = 58,
                Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) });
        else if (Uri.TryCreate(monster.ImageUrl, UriKind.Absolute, out var uri))
            header.Children.Add(new Image { Source = new BitmapImage(uri), Width = 58, Height = 58, Stretch = Stretch.Uniform, Margin = new Thickness(0,0,10,0) });
        var title = new StackPanel(); title.Children.Add(Heading(monster.Name, 20));
        title.Children.Add(Meta($"#{monster.Id} · Lv {monster.Level}\n{monster.Race} · {monster.Element} · {monster.Size}"));
        header.Children.Add(title); DetailsPanel.Children.Add(header);
        AddFavoriteAction(new(DatabaseSearchKind.Monsters, monster.Id.ToString(), monster.Name));
        ShowMonsterStatistics(monster);
        ShowMonsterSkills(monster.Skills);
        AddLine(T("Средняя стоимость обычного дропа у NPC", "Expected NPC value of regular drops"),
            $"{_database.TypicalNpcDropValue(monster):N2} z");
        AddSection(T($"Дроп ({monster.Drops.Count})", $"Drops ({monster.Drops.Count})"));
        foreach (var drop in monster.Drops.OrderByDescending(x => ParseRate(x.Rate)))
            AddLink($"{drop.Name} — {drop.Rate}{(drop.IsMvp ? " MVP" : "")}{(_database.Version == GameDatabaseVersion.Toc && drop.IsMvp ? T(" · базовый шанс", " · base rate") : "")}", () => OpenItem(drop.ItemId));
        AddSection(T($"Локации ({monster.Spawns.Count})", $"Maps ({monster.Spawns.Count})"));
        foreach (var spawn in monster.Spawns.OrderByDescending(x => ParseCount(x.Count)))
            AddLink(SpawnLabel(_database.Version == GameServerProfile.RagnarokPlus ? spawn.Code : spawn.Name, spawn.Count, spawn.Respawn), () => OpenMap(spawn.Code));
    }

    private void ShowItem(DatabaseItem item, SpringRoItemDetails? detail, bool loading)
    {
        _appearanceRedraw=()=>{DetailsPanel.Children.Clear();ShowItem(item,detail,loading);};
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var imageUrl = detail?.ImageUrl;
        if (string.IsNullOrWhiteSpace(imageUrl)) imageUrl = item.ImageUrl;
        if (string.IsNullOrWhiteSpace(imageUrl) && _database.Version != GameServerProfile.RagnarokPlus)
            imageUrl = $"https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/items/{item.Id}.png";
        BitmapSource? imageSource = null;
        if (_database.Version == GameServerProfile.RagnarokPlus)
        {
            if (item.LocalImagePng is { Length: > 0 } bytes)
                imageSource = DecodeCatalogImage(bytes);
        }
        else if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
            imageSource = new BitmapImage(imageUri);
        if (imageSource is not null)
            header.Children.Add(new Image { Source = imageSource, Width = 56, Height = 56,
                Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) });
        var title = new StackPanel();
        var itemHeading = Heading(item.Name, 20); itemHeading.MaxWidth = 180;
        title.Children.Add(itemHeading);
        title.Children.Add(Meta($"Item ID #{item.Id} · {item.Type}"));
        header.Children.Add(title);
        if (_database.Version == GameServerProfile.RagnarokPlus)
        {
            var tags = new WrapPanel();
            if (RagnarokPlusRecipes.ForItem(item.Id).Any()) tags.Children.Add(CraftTag("craftable"));
            if (RagnarokPlusRecipes.UsingItem(item.Id).Any()) tags.Children.Add(CraftTag("mats"));
            if (tags.Children.Count > 0) title.Children.Add(tags);
        }
        DetailsPanel.Children.Add(header);
        AddItemActions(item);

        var server = _settings.ServerProfile;
        var wanted = new CheckBox
        {
            Content = T("Желаемый лут", "Wanted loot"),
            IsChecked = _settings.WantedLootByServer.TryGetValue(server, out var saved) && saved.Contains(item.Id),
            Margin = new Thickness(0, 7, 0, 2)
        };
        wanted.Click += (_, _) =>
        {
            if (!_settings.WantedLootByServer.TryGetValue(server, out var ids))
                _settings.WantedLootByServer[server] = ids = [];
            if (wanted.IsChecked == true) ids.Add(item.Id);
            else ids.Remove(item.Id);
            (Application.Current as App)?.SaveDatabasePreferences();
        };
        DetailsPanel.Children.Add(wanted);
        AddMarketComparison(item);
        if (_database.Version == GameServerProfile.RagnarokPlus) ShowCrafting(item);

        AddSection(T("Информация", "Information"));
        AddItemWeight(item, detail);
        if (_database.Version == GameDatabaseVersion.Toc) ShowTocItemInformation(item);
        else if (loading)
            DetailsPanel.Children.Add(Meta(T("Загружаем актуальные данные SpringRO…", "Loading current SpringRO data…")));
        else if (detail is not null)
        {
            AddLine(T("Цена продажи NPC", "NPC sell value"), detail.NpcSellValue.Length > 0 ? detail.NpcSellValue : "—");
            if (_database.Version == GameDatabaseVersion.SpringRo)
                AddLine(T("Количество на сервере", "Copies on server"), detail.ServerCopies.Length > 0 ? detail.ServerCopies : "—");
            if (!string.IsNullOrWhiteSpace(detail.Description))
            {
                AddSection(T("Описание и эффекты", "Description & effects"));
                AddExpandableDescription(detail.Description);
            }
        }
        else DetailsPanel.Children.Add(Meta(T("Актуальные данные временно недоступны.", "Current data is temporarily unavailable.")));
        AddEquipmentReferences(item, detail?.Description ?? item.Description);
        if (_database.Version == GameServerProfile.RagnarokPlus)
            AddLink(T("Открыть карточку и рынок Ragnarok+", "Open Ragnarok+ item and market"), () =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        $"https://ragnarok.plus/item-db/{item.Id}") { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    DebugLog.Write($"RAGNAROK+ ITEM LINK ERROR: {ex.Message}");
                }
            });

        var sources = item.DroppedBy.OrderByDescending(x => ParseRate(x.Rate))
            .GroupBy(x => (x.MonsterId, SeparateMvp: _database.Version != GameServerProfile.RagnarokPlus && x.IsMvp))
            .ToList();
        AddSection(T($"Источники ({sources.Count})", $"Sources ({sources.Count})"));
        if (sources.Count == 0) DetailsPanel.Children.Add(Meta(T($"Нет в дроп-листах монстров {_database.Version}.", $"Not found in {_database.Version} monster drop lists.")));
        const int pageSize = 10;
        var sourceRows = new StackPanel();
        DetailsPanel.Children.Add(sourceRows);
        var shown = 0;
        var more = new Button { Content = T("Показать ещё", "Show more") };
        var view = _itemCardView;
        more.Click += (_, _) =>
        {
            ShowNextSources(pageSize);
            if (view is not null) view.SourceCount = shown;
        };
        ShowNextSources(view?.SourceCount ?? pageSize);
        if (sources.Count > pageSize) DetailsPanel.Children.Add(more);

        void ShowNextSources(int count)
        {
            foreach (var group in sources.Skip(shown).Take(count))
            {
                var source = group.First();
                var rates = group.Select(x => $"{x.Rate}{(x.IsMvp ? " MVP" : "")}").Distinct();
                var note = _database.Version == GameDatabaseVersion.Toc && source.IsMvp
                    ? T(" · базовый шанс", " · base rate") : "";
                AddLink($"{source.Name} — {string.Join(" / ", rates)}{note}", () => OpenMonster(source.MonsterId), sourceRows);
                shown++;
            }
            more.Visibility = shown < sources.Count ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static BitmapFrame DecodeCatalogImage(byte[] png)
    {
        using var stream = new MemoryStream(png);
        return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private void ShowMap(DatabaseMap map)
    {
        _appearanceRedraw=()=>{DetailsPanel.Children.Clear();ShowMap(map);};
        var density = _database.GetMapExperienceDensity(map);
        DetailsPanel.Children.Add(Heading(map.Name, 20));
        DetailsPanel.Children.Add(Meta(map.Code));
        AddFavoriteAction(new(DatabaseSearchKind.Maps, map.Code, map.Name));
        if (map.LocalImagePng is { Length: > 0 } minimap)
            DetailsPanel.Children.Add(new Image { Source = DecodeCatalogImage(minimap), MaxWidth = 256,
                MaxHeight = 256, Stretch = Stretch.Uniform });
        AddFarmPlanner(map);
        AddSection(T("Плотность опыта", "Experience density"));
        AddLine(T("Монстров на карте", "Monsters on map"), $"{density.MonsterCount:N0}");
        AddLine(T("Средний Base EXP за моба", "Average Base EXP per mob"), $"{density.BaseExperience:N0}");
        AddLine(T("Средний Job EXP за моба", "Average Job EXP per mob"), $"{density.JobExperience:N0}");
        AddSection(T($"Монстры ({map.Monsters.Count})", $"Monsters ({map.Monsters.Count})"));
        foreach (var mob in map.Monsters.OrderByDescending(x => ParseCount(x.Count)))
            AddLink(SpawnLabel(mob.Name, mob.Count, mob.Respawn), () => OpenMonster(mob.MonsterId));
    }

    private void ShowMarketListing(MarketListing listing)
    {
        _appearanceRedraw=()=>{DetailsPanel.Children.Clear();ShowMarketListing(listing);};
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
        if (_database.Version != GameServerProfile.RagnarokPlus || listing.MapImagePng is not null)
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
        double? left = null;
        double? top = null;
        if (listing.MapImagePng is { Length: > 0 } png &&
            listing.MapPinLeft is >= 0 and <= 1 && listing.MapPinTop is >= 0 and <= 1)
        {
            using var stream = new MemoryStream(png);
            var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            canvas.Children.Add(new Image { Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform });
            left = listing.MapPinLeft.Value * size;
            top = listing.MapPinTop.Value * size;
        }
        else if (_database.Version != GameServerProfile.RagnarokPlus)
        {
            var mapUrl = $"https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/minimaps/{listing.Map}.png";
            if (Uri.TryCreate(mapUrl, UriKind.Absolute, out var uri))
                canvas.Children.Add(new Image { Source = new BitmapImage(uri), Width = size, Height = size, Stretch = Stretch.Uniform });
            if (listing.MapWidth > 0 && listing.MapHeight > 0)
            {
                var maximum = Math.Max(listing.MapWidth, listing.MapHeight);
                left = (listing.X + (maximum - listing.MapWidth) / 2d) / maximum * size;
                top = ((maximum - listing.MapHeight) / 2d + listing.MapHeight - listing.Y) / maximum * size;
            }
        }

        if (left.HasValue && top.HasValue)
        {
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = 15, Height = 15, Fill = Brushes.Red, Stroke = Brushes.White, StrokeThickness = 3
            };
            Canvas.SetLeft(ring, left.Value - ring.Width / 2);
            Canvas.SetTop(ring, top.Value - ring.Height / 2);
            Panel.SetZIndex(ring, 2);
            canvas.Children.Add(ring);
        }

        DetailsPanel.Children.Add(new Border
        {
            Child = canvas, BorderBrush = AppearanceService.Brush(this,"#A9C0DD","Border"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(3), Margin = new Thickness(0, 7, 0, 5), HorizontalAlignment = HorizontalAlignment.Center
        });
    }

    private readonly Dictionary<string, List<(DatabaseSearchKind Kind, string Id, string Name)>> _recentCards = [];

    private void RememberCard(object value)
    {
        (DatabaseSearchKind Kind, string Id, string Name)? card = value switch
        {
            DatabaseItem item => (DatabaseSearchKind.Items, item.Id.ToString(), item.Name),
            DatabaseMonster monster => (DatabaseSearchKind.Monsters, monster.Id.ToString(), monster.Name),
            DatabaseMap map => (DatabaseSearchKind.Maps, map.Code, map.Name),
            _ => null
        };
        if (card is not { } entry) return;
        if (!_recentCards.TryGetValue(_database.Version, out var recent))
            _recentCards[_database.Version] = recent = [];
        recent.RemoveAll(row => row.Kind == entry.Kind && row.Id == entry.Id);
        recent.Insert(0, entry);
        if (recent.Count > 30) recent.RemoveAt(30);
    }

    internal void ShowRecentCards()
    {
        Navigate(Render);
        Render();

        void Render()
        {
            ++_detailVersion;
            Clear();
            RefreshBackButton();
            DetailsScroll.ScrollToTop();
            AddSection(T("Недавние карточки", "Recent cards"));
            if (!_recentCards.TryGetValue(_database.Version, out var recent) || recent.Count == 0)
            {
                DetailsPanel.Children.Add(Meta(T("Нет открытых карточек.", "No recently opened cards.")));
                return;
            }
            foreach (var card in recent)
                AddLink($"{card.Name} · {card.Id}", () =>
                {
                    if (card.Kind == DatabaseSearchKind.Maps) OpenMap(card.Id);
                    else if (int.TryParse(card.Id, out var id))
                    {
                        if (card.Kind == DatabaseSearchKind.Items) OpenItem(id);
                        else OpenMonster(id);
                    }
                });
        }
    }

    private void OpenMonster(int id) { var value = _database.Monster(id); if (value is not null) ShowDetails(value); }
    private void OpenItem(int id) { var value = _database.Item(id); if (value is not null) ShowDetails(value); }
    private void OpenMap(string code) { var value = _database.Map(code); if (value is not null) ShowDetails(value); }
    private void AddSection(string text) => DetailsPanel.Children.Add(new TextBlock { Text = text, FontFamily = new FontFamily("Georgia"), FontWeight = FontWeights.Bold, FontSize = 16, Foreground = AppearanceService.Brush(this,"#174E91","Foreground"), Margin = new Thickness(0,15,0,5) });
    private void AddLine(string name, string value) => DetailsPanel.Children.Add(new TextBlock { Text = $"{name}: {value}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,2,0,2), FontSize = 12 });
    private static string SpawnLabel(string name, string count, string respawn) =>
        string.Join(" · ", new[] { name, count, respawn }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private void AddLink(string text, Action action, Panel? parent = null)
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        var button = new Button
        {
            Content = label, HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(7, 4, 7, 4), FontSize = 11
        };
        button.Click += (_, _) => action();
        (parent ?? DetailsPanel).Children.Add(button);
    }
    private TextBlock Heading(string text, double size) => new() { Text = text, FontFamily = new FontFamily("Georgia"), FontWeight = FontWeights.Bold, FontSize = size, Foreground = AppearanceService.Brush(this,"#174E91","Foreground"), TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
    private TextBlock Meta(string text) => new() { Text = text, Foreground = AppearanceService.Brush(this,"#5B7496","Foreground"), Margin = new Thickness(0,4,0,4), TextWrapping = TextWrapping.Wrap };
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
