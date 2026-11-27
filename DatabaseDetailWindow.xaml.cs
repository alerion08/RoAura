using System.Windows.Controls.Primitives;
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
        UpdateDatabasePalette();
        RefreshLanguage();
        Loaded+=(_,_)=>{AppearanceService.Changed-=AppearanceChanged;AppearanceService.Changed+=AppearanceChanged;AppearanceChanged();};
        Closed+=(_,_)=>AppearanceService.Changed-=AppearanceChanged;
    }

    public void RefreshLanguage()
    {
        RefreshPanelTitle();
        KeyboardStatus.Text = T($"Ввод в поиск активен · {_settings.DatabaseHotkey} — закрыть",
            $"Search input active · {_settings.DatabaseHotkey} — close");
        BackButton.Content = T("‹ Назад", "‹ Back");
        FavoriteButton.ToolTip = T("Добавить в избранное", "Add to favorites");
        System.Windows.Automation.AutomationProperties.SetName(FavoriteButton, FavoriteButton.ToolTip.ToString());
        CreateFavoriteListButton.Content = T("Создать новый +", "Create new +");
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

    public void Clear()
    {
        _displayedEntity = null;
        _cardLoading = false;
        RefreshPanelTitle();
        _favoriteEntry = null;
        FavoriteMenu.Visibility = Visibility.Collapsed;
        FavoriteButton.Visibility = Visibility.Collapsed;
        if (_itemActionsMenu is not null) _itemActionsMenu.IsOpen = false;
        _itemActionsMenu = null;
        DetailsPanel.Children.Clear();
    }
    private void AppearanceChanged()
    {
        UpdateDatabasePalette();
    }

    private object? _displayedEntity;
    private bool _cardLoading;
    private void RefreshPanelTitle()
    {
        PanelTitle.Text = _displayedEntity switch
        {
            DatabaseMonster => T("Монстр", "Monster"),
            DatabaseItem => T("Предмет", "Item"),
            DatabaseMap => T("Карта", "Map"),
            MarketListing => T("Предложение", "Listing"),
            RagnarokPlusBarterCatalog.Offer => T("Обмен", "Exchange"),
            _ => T("Карточка", "Card")
        };
    }

    private readonly Stack<(Action Page, Action Restore, double Offset)> _back = new();
    private Action? _currentPage;

    internal void ResetDetails()
    {
        ++_detailVersion;
        _back.Clear();
        _currentPage = null;
        BackButton.Visibility = Visibility.Collapsed;
        Clear();
    }

    internal Action CaptureQuestLinkReturn()
    {
        var page = _currentPage;
        var back = _back.Reverse().ToArray();
        var offset = DetailsScroll.VerticalOffset;
        var restore = page is null ? null : CaptureCardView(page);
        return () =>
        {
            if (ReferenceEquals(page, _currentPage)) return;
            ResetDetails();
            foreach (var previous in back) _back.Push(previous);
            _currentPage = page;
            restore?.Invoke();
            RestoreScroll(offset, _detailVersion);
        };
    }

    private void Navigate(Action page)
    {
        if (_currentPage is { } previous)
            _back.Push((previous, CaptureCardView(previous), DetailsScroll.VerticalOffset));
        _currentPage = page;
    }

    private Action CaptureCardView(Action rebuild)
    {
        // Keep the form, expanded groups, rendered prices and source rows for real cards.
        // A SpringRO item still awaiting its details should resume the normal load path.
        if (_displayedEntity is null || _cardLoading) return rebuild;
        var controls = DetailsPanel.Children.Cast<UIElement>().ToArray();
        var entity = _displayedEntity;
        var itemView = _itemCardView;
        var favorite = _favoriteEntry;
        var actionsMenu = _itemActionsMenu;
        return () =>
        {
            ++_detailVersion;
            Clear();
            _cardLoading = false;
            _displayedEntity = entity;
            _itemCardView = itemView;
            _itemActionsMenu = actionsMenu;
            foreach (var control in controls) DetailsPanel.Children.Add(control);
            if (favorite is not null) SetFavoriteEntry(favorite);
            RefreshPanelTitle();
            RefreshBackButton();
        };
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
        previous.Restore();
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
        _displayedEntity = value;
        RefreshPanelTitle();
        switch (value)
        {
            case DatabaseMonster monster: ShowMonster(monster); break;
            case DatabaseItem item when _database.Version is GameDatabaseVersion.Toc or GameServerProfile.RagnarokPlus:
                ShowItem(item, new SpringRoItemDetails { ImageUrl = item.ImageUrl, Description = item.Description,
                    NpcSellValue = _database.Version == GameServerProfile.RagnarokPlus &&
                        !item.RagnarokPlusDetailsCaptured && item.NpcSellPrice == 0 ? "" : item.NpcSellPrice.ToString("N0") }, false);
                break;
            case DatabaseItem item:
                _cardLoading = true;
                ShowItem(item, null, true);
                try
                {
                    var detail = await _itemDetails.GetAsync(item.Id);
                    if (version != _detailVersion) return;
                    Clear();
                    _displayedEntity = item;
                    RefreshPanelTitle();
                    RefreshBackButton();
                    ShowItem(item, detail, false);
                    if (_returnScroll.Version == version) RestoreScroll(_returnScroll.Offset, version);
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"ITEM DETAILS LOAD ERROR #{item.Id}: {ex.Message}");
                    if (version != _detailVersion) return;
                    Clear();
                    _displayedEntity = item;
                    RefreshPanelTitle();
                    RefreshBackButton();
                    ShowItem(item, null, false);
                    if (_returnScroll.Version == version) RestoreScroll(_returnScroll.Offset, version);
                }
                break;
            case RagnarokPlusBarterCatalog.Offer offer: ShowBarter(offer); break;
            case DatabaseMap map: ShowMap(map); break;
            case MarketListing listing: ShowMarketListing(listing); break;
        }
    }

    private void ShowMonster(DatabaseMonster monster)
    {
        AddEntityHeader(monster.Name, $"#{monster.Id} · Lv {monster.Level}\n{monster.Race} · {monster.Element} · {monster.Size}",
            monster.LocalImagePng, "♞", monster.ImageUrl);
        SetFavoriteEntry(new(DatabaseSearchKind.Monsters, monster.Id.ToString(), monster.Name));
        AddMetrics(("HP", DatabasePresentation.Count(monster.Hp)), ("Base EXP", DatabasePresentation.Count(monster.BaseExp)),
            ("Job EXP", DatabasePresentation.Count(monster.JobExp)));
        AddSection(T($"Дроп ({monster.Drops.Count})", $"Drops ({monster.Drops.Count})"));
        AddLine(T("Средняя стоимость обычного дропа у NPC", "Expected NPC value of regular drops"),
            $"{_database.TypicalNpcDropValue(monster):N2} z");
        foreach (var drop in monster.Drops.OrderByDescending(x => ParseRate(x.Rate)))
        {
            var item = _database.Item(drop.ItemId);
            var mvp = drop.IsMvp ? (_database.Version == GameDatabaseVersion.Toc ? T("MVP · базовый шанс", "MVP · base rate") : "MVP") : "";
            AddReferenceRow(drop.Name, mvp, drop.Rate, item?.LocalImagePng, "◇", item is null ? null : () => OpenItem(drop.ItemId));
        }
        AddSection(T($"Локации ({monster.Spawns.Count})", $"Maps ({monster.Spawns.Count})"));
        foreach (var spawn in monster.Spawns.OrderByDescending(x => ParseCount(x.Count)))
        {
            var map = _database.Map(spawn.Code);
            AddReferenceRow(_database.Version == GameServerProfile.RagnarokPlus ? spawn.Code : spawn.Name,
                spawn.Respawn, spawn.Count, map?.LocalImagePng, "▧", map is null ? null : () => OpenMap(spawn.Code));
        }
        var combat = new StackPanel();
        ShowMonsterStatistics(monster, combat);
        CollapsedSection(T("Боевые характеристики", "Combat stats"), combat);
        if (monster.Skills.Count > 0)
        {
            var skills = new StackPanel();
            ShowMonsterSkills(monster.Skills, skills);
            CollapsedSection(T("Навыки и дополнительные данные", "Skills and additional data"), skills);
        }
    }

    private void ShowItem(DatabaseItem item, SpringRoItemDetails? detail, bool loading, RagnarokPlusBarterCatalog.Offer? barter = null)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 42, 0) };
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
            header.Children.Add(new Image { Source = imageSource, Width = 46, Height = 46,
                Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) });
        if (imageSource is null) header.Children.Add(EntityIcon(null, "◇"));
        var title = new StackPanel();
        var itemHeading = Heading(item.Name, 23);
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
        var actions = new UniformGrid { Rows = 1, Margin = new Thickness(0, 5, 0, 0) };
        DetailsPanel.Children.Add(actions);
        AddMarketComparison(item, actions);
        AddLootListAction(item, actions);
        AddItemActions(item, actions);

        AddSection(T("Информация", "Information"));
        AddItemWeight(item, detail);
        if (_database.Version == GameServerProfile.RagnarokPlus)
        {
            ShowCrafting(item);
            var quests = RagnarokPlusQuestCatalog.Instance.RequiredFor(_database.Version, item.Id).ToArray();
            if (quests.Length > 0)
            {
                var links = new StackPanel();
                AddItemInformationExpander(T($"Нужен для задания ({quests.Length})", $"Needed for quests ({quests.Length})"), links);
                foreach (var quest in quests)
                    AddLink(quest.Title, async () =>
                    {
                        if (Application.Current is App app) await app.OpenQuestFromDatabaseAsync(quest.Key);
                    }, links);
            }
        }
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
                AddExpandableDescription(detail.Description);
            }
        }
        else DetailsPanel.Children.Add(Meta(T("Актуальные данные временно недоступны.", "Current data is temporarily unavailable.")));
        AddEquipmentReferences(item, detail?.Description ?? item.Description);
        if (_database.Version == GameServerProfile.RagnarokPlus)
        {
            var offers = RagnarokPlusBarterCatalog.ForItem(item.Id);
            if (barter is not null) AddBarterSection(barter, DetailsPanel);
            foreach (var offer in offers.Where(offer => offer != barter))
            {
                var expand = new Expander { Header = $"{T("Бартер", "Barter")} · {offer.Npc}" };
                expand.Expanded += (_, _) =>
                {
                    if (expand.Content is not null) return;
                    var panel = new StackPanel();
                    AddBarterSection(offer, panel);
                    expand.Content = panel;
                };
                DetailsPanel.Children.Add(expand);
            }
        }
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
                var monster = _database.Monster(source.MonsterId);
                AddReferenceRow(source.Name, note.Trim(' ', '·'), string.Join(" / ", rates), monster?.LocalImagePng, "♞",
                    monster is null ? null : () => OpenMonster(source.MonsterId), sourceRows);
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
        var density = _database.GetMapExperienceDensity(map);
        AddEntityHeader(map.Name, map.Code, null, "▧");
        SetFavoriteEntry(new(DatabaseSearchKind.Maps, map.Code, map.Name));
        AddMetrics((T("Монстров", "Monsters"), $"{density.MonsterCount:N0}"),
            (T("Средний Base EXP", "Average Base EXP"), $"{density.BaseExperience:N0}"),
            (T("Средний Job EXP", "Average Job EXP"), $"{density.JobExperience:N0}"));
        if (map.LocalImagePng is { Length: > 0 } minimap)
            DetailsPanel.Children.Add(new Image { Source = DecodeCatalogImage(minimap), MaxWidth = 230,
                MaxHeight = 230, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 7, 0, 7) });
        AddSection(T($"Монстры ({map.Monsters.Count})", $"Monsters ({map.Monsters.Count})"));
        foreach (var mob in map.Monsters.OrderByDescending(x => ParseCount(x.Count)))
        {
            var monster = _database.Monster(mob.MonsterId);
            AddReferenceRow(mob.Name, mob.Respawn, mob.Count, monster?.LocalImagePng, "♞",
                monster is null ? null : () => OpenMonster(mob.MonsterId));
        }
        AddFarmPlanner(map);
    }

    private void ShowMarketListing(MarketListing listing)
    {
        var item = _database.Item(listing.ItemId);
        AddEntityHeader(listing.ItemName, $"#{listing.ItemId} · {(listing.ShopType == "BUY" ? T("Скупка", "Buying") : T("Продажа", "Selling"))} · {listing.GameMode}",
            item?.LocalImagePng, "◇");
        var price = SpringRoMarketService.ParsePrice(listing.Price);
        var quantity = SpringRoMarketService.ParsePrice(listing.Quantity);
        AddMetrics((T("Цена за ед.", "Unit price"), DatabasePresentation.Count(listing.Price)),
            (T("Количество", "Quantity"), DatabasePresentation.Count(listing.Quantity)),
            (T("Сумма лота", "Lot total"), price is > 0 and < long.MaxValue && quantity is > 0 and < long.MaxValue
                ? $"{(System.Numerics.BigInteger)price * quantity:N0}" : "—"));
        if (item is not null)
            AddReferenceRow(item.Name, "", $"#{item.Id}", item.LocalImagePng, "◇", () => OpenItem(item.Id));
        AddLine(T("Заточка", "Refine"), listing.Refine);
        AddLine(T("Карты", "Cards"), listing.Cards);
        AddLine(T("Магазин", "Stall"), listing.Stall);
        AddLine(T("Владелец", "Owner"), listing.Owner);
        AddSection(T("Местоположение", "Location"));
        if (!string.IsNullOrWhiteSpace(listing.Map))
        {
            AddNavigationRow(DetailsPanel, $"{listing.Map} ({listing.X}, {listing.Y})", listing.NavigationCommand);
            if (_database.Version != GameServerProfile.RagnarokPlus || listing.MapImagePng is not null)
                AddMarketMap(listing);
        }
    }

    private void AddMarketMap(MarketListing listing)
    {
        const double size = 200;
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
                Width = 12, Height = 12, Fill = Brushes.Firebrick, Stroke = Brushes.White, StrokeThickness = 2
            };
            Canvas.SetLeft(ring, left.Value - ring.Width / 2);
            Canvas.SetTop(ring, top.Value - ring.Height / 2);
            Panel.SetZIndex(ring, 2);
            canvas.Children.Add(ring);
        }

        DetailsPanel.Children.Add(new Border
        {
            Child = canvas, BorderBrush = AppearanceService.Brush(this,"#A9C0DD","Border"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(3), Margin = new Thickness(0, 7, 0, 5), HorizontalAlignment = HorizontalAlignment.Left
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
            PanelTitle.Text = T("Недавно просмотрено", "Recently viewed");
            AddSection(T("Недавно просмотрено", "Recently viewed"));
            if (!_recentCards.TryGetValue(_database.Version, out var recent) || recent.Count == 0)
            {
                DetailsPanel.Children.Add(Meta(T("Нет открытых карточек.", "No recently opened cards.")));
                return;
            }
            foreach (var card in recent)
            {
                var itemId = int.TryParse(card.Id, out var parsed) ? parsed : 0;
                var sprite = card.Kind switch
                {
                    DatabaseSearchKind.Items => _database.Item(itemId)?.LocalImagePng,
                    DatabaseSearchKind.Monsters => _database.Monster(itemId)?.LocalImagePng,
                    _ => _database.Map(card.Id)?.LocalImagePng
                };
                var icon = card.Kind == DatabaseSearchKind.Monsters ? "♞" : card.Kind == DatabaseSearchKind.Items ? "◇" : "▧";
                AddReferenceRow(card.Name, card.Id, "", sprite, icon, () =>
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
    }

    private void OpenMonster(int id) { var value = _database.Monster(id); if (value is not null) ShowDetails(value); }
    private void OpenItem(int id) { var value = _database.Item(id); if (value is not null) ShowDetails(value); }
    private void OpenMap(string code) { var value = _database.Map(code); if (value is not null) ShowDetails(value); }
    private void AddSection(string text)
    {
        var heading = new TextBlock { Text = text, FontWeight = FontWeights.Bold, FontSize = 13,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 5) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "DatabaseBlue");
        DetailsPanel.Children.Add(heading);
    }
    private void AddLine(string name, string value) => DetailsPanel.Children.Add(new TextBlock { Text = $"{name}: {value}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,2,0,2), FontSize = 12 });
    private static string SpawnLabel(string name, string count, string respawn) =>
        string.Join(" · ", new[] { name, count, respawn }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private void AddLink(string text, Action action, Panel? parent = null) =>
        AddReferenceRow(text, "", "", null, "", action, parent);

    private TextBlock Heading(string text, double size)
    {
        var label = new TextBlock { Text = text, FontFamily = new FontFamily("Georgia"), FontWeight = FontWeights.Bold,
            FontSize = size, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, "DatabaseBlue");
        return label;
    }

    private TextBlock Meta(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 11, Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, "DatabaseMuted");
        return label;
    }

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
