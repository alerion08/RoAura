using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.ComponentModel;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseOverlayWindow : Window
{
    public event Action? HideRequested;
    private readonly AppSettings _settings;
    private readonly GameDatabaseService _database;
    private readonly DatabaseDetailWindow _details;
    private readonly SpringRoMarketService _market;
    private RagnarokPlusMarketWindow? _ragnarokPlusMarket;
    private RagnarokPlusMarketWindow? _ragnarokPlusCatalog;
    private CancellationTokenSource? _catalogSync;
    private bool _catalogComplete;
    private bool _catalogDetailsComplete;
    private DateTimeOffset _nextCatalogAttempt;
    private readonly DispatcherTimer _visibilityTimer;
    private readonly DispatcherTimer _marketRefreshTimer;
    private DatabaseSearchKind _kind = DatabaseSearchKind.Monsters;
    private ForegroundProcess.WindowBounds _gameBounds;
    private bool _temporarilyHidden;
    private DateTime _lastRelevantForegroundUtc = DateTime.UtcNow;
    private CancellationTokenSource? _marketSearchCancellation;
    private int _marketSearchVersion;
    private readonly Dictionary<DatabaseSearchKind, string> _searchQueries = new()
    {
        [DatabaseSearchKind.Monsters] = "",
        [DatabaseSearchKind.Items] = "",
        [DatabaseSearchKind.Maps] = "",
        [DatabaseSearchKind.Market] = ""
    };
    private bool _restoringSearchQuery;
    private bool _marketRefreshRunning;
    private bool _isTopmost;
    public bool IsDatabaseOpen { get; private set; }

    public DatabaseOverlayWindow(AppSettings settings, GameDatabaseService database, SpringRoMarketService market)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _market = market ?? throw new ArgumentNullException(nameof(market));
        InitializeComponent();
        _details = new DatabaseDetailWindow(settings, database);
        _details.HideRequested += () => HideRequested?.Invoke();
        _market.DataUpdated += Market_DataUpdated;
        RefreshLanguage();
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
            RefreshResults();
        };
        _visibilityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _visibilityTimer.Tick += (_, _) => RefreshOverlayVisibility();
        _marketRefreshTimer = new DispatcherTimer { Interval = MarketRefreshInterval() };
        _marketRefreshTimer.Tick += async (_, _) => await RefreshMarketInBackgroundAsync();
        _marketRefreshTimer.Start();
        Dispatcher.BeginInvoke(async () => await RefreshMarketInBackgroundAsync(), DispatcherPriority.Background);
        _settings.PropertyChanged += Settings_PropertyChanged;
    }

    public void RefreshServer()
    {
        HideDatabase();
        _details.ResetServer();
        foreach (var key in _searchQueries.Keys.ToList()) _searchQueries[key] = "";
        SearchBox.Text = "";
        _kind = DatabaseSearchKind.Monsters;
        MonstersMode.IsChecked = true;
        RefreshLanguage();
        RefreshResults();
    }

    public void RefreshLanguage()
    {
        var raceIndex = Math.Max(0, RaceFilter.SelectedIndex);
        var elementIndex = Math.Max(0, ElementFilter.SelectedIndex);
        var sizeIndex = Math.Max(0, SizeFilter.SelectedIndex);
        OverlayTitle.Text = T($"База {_database.Version}", $"{_database.Version} Database");
        MarketMode.IsEnabled = GameServerProfile.SupportsMarket(_settings.ServerProfile);
        MarketMode.Opacity = MarketMode.IsEnabled ? 1 : 0.6;
        MonstersMode.Content = T("Монстры", "Monsters");
        ItemsMode.Content = T("Предметы", "Items");
        MapsMode.Content = T("Карты", "Maps");
        MarketMode.Content = T("Маркет", "Market");
        MarketSellMode.Content = T("Продажа", "Selling");
        MarketBuyMode.Content = T("Покупка", "Buying");
        SearchButton.Content = T("Найти", "Search");
        MarketTypeLabel.Text = T("Тип магазина:", "Shop type:");
        var ragnarokPlus = _settings.ServerProfile == GameServerProfile.RagnarokPlus;
        RagnarokPlusSignInButton.Content = T("Войти", "Sign in");
        RagnarokPlusSignInButton.Visibility = ragnarokPlus ? Visibility.Visible : Visibility.Collapsed;
        AdvancedHeaderText.Text = T("Расширенный поиск", "Advanced search");
        RaceLabel.Text = T("Раса", "Race");
        ElementLabel.Text = T("Элемент", "Element");
        SizeLabel.Text = T("Размер", "Size");
        ElementLevelLabel.Text = T("Уровень элемента, от", "Element level, min");
        LevelMinLabel.Text = T("Уровень, от", "Level, min");
        LevelMaxLabel.Text = T("Уровень, до", "Level, max");
        BaseExpMinLabel.Text = T("Base EXP, от", "Base EXP, min");
        JobExpMinLabel.Text = T("Job EXP, от", "Job EXP, min");
        HpMinLabel.Text = T("HP, от", "HP, min");
        HpMaxLabel.Text = T("HP, до", "HP, max");
        AggressiveFilter.Content = T("Агрессивный", "Aggressive");
        CloseButton.ToolTip = T("Закрыть базу данных", "Close database");
        RaceFilter.ItemsSource = new[] { T("Все расы", "All races") }.Concat(_database.MonsterRaces).ToList();
        ElementFilter.ItemsSource = new[] { T("Все элементы", "All elements") }.Concat(_database.MonsterElements).ToList();
        SizeFilter.ItemsSource = new[] { T("Все размеры", "All sizes") }.Concat(_database.MonsterSizes).ToList();
        RaceFilter.SelectedIndex = Math.Min(raceIndex, RaceFilter.Items.Count - 1);
        ElementFilter.SelectedIndex = Math.Min(elementIndex, ElementFilter.Items.Count - 1);
        SizeFilter.SelectedIndex = Math.Min(sizeIndex, SizeFilter.Items.Count - 1);
        _details.RefreshLanguage();
    }

    public void PrepareForFirstUse()
    {
        // Create both native windows at application startup. This removes the one-off
        // HWND/DWM initialization race that otherwise occurs on the first database open.
        _ = new WindowInteropHelper(this).EnsureHandle();
        _ = new WindowInteropHelper(_details).EnsureHandle();
        _isTopmost = true;
        ReleaseTopmost();
    }

    public void ShowForGame()
    {
        var game = ResolveGameWindow();
        _gameBounds = game;
        PlacePanels(game);

        IsDatabaseOpen = true;
        _temporarilyHidden = false;
        _lastRelevantForegroundUtc = DateTime.UtcNow;
        ShowPanel(this);
        ShowPanel(_details);
        SetPanelsVisible(true);
        ForceTopmost();
        _visibilityTimer.Start();
        Activate();
        SearchBox.Focus();
    }

    public void HideDatabase()
    {
        _marketSearchCancellation?.Cancel();
        _marketSearchCancellation = null;
        _marketSearchVersion++;
        IsDatabaseOpen = false;
        _visibilityTimer.Stop();
        Hide();
        _details.Hide();
    }

    /// <summary>
    /// Show() on a window whose native handle was destroyed throws. The handle is
    /// created once at startup; if it is gone, log and skip instead of crashing.
    /// </summary>
    private static void ShowPanel(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && !IsWindow(handle))
        {
            DebugLog.Write($"DATABASE OVERLAY: native window {window.GetType().Name} was destroyed; Show() skipped.");
            return;
        }
        window.Show();
    }

    private void RefreshOverlayVisibility()
    {
        if (!IsDatabaseOpen) return;
        var foreground = GetForegroundWindow();
        var searchHandle = new WindowInteropHelper(this).Handle;
        var detailHandle = _details.Handle;
        var databaseHasFocus = foreground == searchHandle || foreground == detailHandle ||
            OwnedWindows.Cast<Window>().Any(w => w.IsActive) || _details.OwnedWindows.Cast<Window>().Any(w => w.IsActive);

        if (ForegroundProcess.TryGetForegroundAllowedBounds(_settings.AllowedProcesses, out var game))
        {
            _lastRelevantForegroundUtc = DateTime.UtcNow;
            if (!game.Equals(_gameBounds))
            {
                _gameBounds = game;
                PlacePanels(game);
            }
            SetPanelsVisible(true);
            // Gepard/fullscreen clients may silently move the game ahead of both
            // panels on any input event. The cached flag only describes our window
            // style, not the real order inside the topmost band, so restore the
            // order on every visibility tick without activating either panel.
            PromotePanelsAboveGame();
        }
        else if (databaseHasFocus)
        {
            _lastRelevantForegroundUtc = DateTime.UtcNow;
            SetPanelsVisible(true);
            ForceTopmost();
        }
        else if (DateTime.UtcNow - _lastRelevantForegroundUtc > TimeSpan.FromMilliseconds(450))
        {
            ReleaseTopmost();
            if (ForegroundProcess.DoesForegroundWindowCover(_gameBounds)) SetPanelsVisible(false);
        }
    }

    private void PlacePanels(ForegroundProcess.WindowBounds game)
    {
        // WindowBounds are physical screen pixels; WPF Left/Top/Width/Height are
        // device-independent units. Convert once here so that both panels (the
        // detail window positions itself from the bounds we pass in) stay inside
        // the client area on high-DPI displays.
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new Point(game.Left, game.Top));
        var bottomRight = fromDevice.Transform(new Point(game.Right, game.Bottom));
        var gameWidth = Math.Max(1, bottomRight.X - topLeft.X);
        var gameHeight = Math.Max(1, bottomRight.Y - topLeft.Y);
        var dipBounds = new ForegroundProcess.WindowBounds(
            (int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
            (int)Math.Round(bottomRight.X), (int)Math.Round(bottomRight.Y));

        var margin = Math.Clamp(gameWidth * .008, 6, 14);
        var panelWidth = Math.Clamp(gameWidth * .205, 320, 390);
        var panelHeightMaximum = Math.Max(460, gameHeight - margin * 2);
        var panelHeight = Math.Clamp(gameHeight * .82, 460, panelHeightMaximum);
        Width = panelWidth;
        Height = Math.Min(panelHeight, gameHeight);
        Left = topLeft.X + margin;
        Top = topLeft.Y + (gameHeight - Height) / 2;
        _details.PlaceForGame(dipBounds, panelWidth, Height, margin);
        foreach (var dialog in OwnedWindows.Cast<Window>().Concat(_details.OwnedWindows.Cast<Window>()).OfType<MarketWatchDialog>())
            PositionWatchDialog(dialog);
    }

    internal void PositionWatchDialog(MarketWatchDialog dialog)
    {
        var left = Left + Width;
        var right = _details.Left;
        if (right < left) { left = Math.Min(Left, _details.Left); right = Math.Max(Left + Width, _details.Left + _details.Width); }
        var top = Math.Max(Top, _details.Top);
        var bottom = Math.Min(Top + Height, _details.Top + _details.Height);
        dialog.CenterInArea(new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)));
    }

    private void SetPanelsVisible(bool visible)
    {
        if (_temporarilyHidden == !visible) return;
        if (!visible)
        {
            _temporarilyHidden = true;
            Hide();
            _details.Hide();
            return;
        }

        _temporarilyHidden = false;
        ShowActivated = false;
        _details.ShowActivated = false;
        ShowPanel(this);
        ShowPanel(_details);
        ShowActivated = true;
        _details.ShowActivated = true;
    }

    private void ForceTopmost()
    {
        SetWindowPos(new WindowInteropHelper(this).Handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        SetWindowPos(_details.Handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        _isTopmost = true;
    }

    private void PromotePanelsAboveGame()
    {
        var searchHandle = new WindowInteropHelper(this).Handle;
        var detailHandle = _details.Handle;
        if (searchHandle == IntPtr.Zero || detailHandle == IntPtr.Zero) return;

        // Deliberately do not assign the Ragnarok HWND as Owner/GWL_HWNDPARENT:
        // doing that made modifier keys stick in protected clients, and windows
        // owned by the game HWND are destroyed together with it when the game
        // exits.  A non-activating remove/reinsert is enough to restore the
        // overlay after the game is clicked.
        SetWindowPos(searchHandle, HwndNotTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        SetWindowPos(detailHandle, HwndNotTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        ForceTopmost();
    }

    private void ReleaseTopmost()
    {
        if (!_isTopmost) return;
        SetWindowPos(new WindowInteropHelper(this).Handle, HwndNotTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        SetWindowPos(_details.Handle, HwndNotTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        _isTopmost = false;
    }

    private ForegroundProcess.WindowBounds ResolveGameWindow()
    {
        if (ForegroundProcess.TryFindWindow(_settings.AllowedProcesses, out _, out var game))
            return game;
        // No game window: fall back to the primary monitor in physical pixels
        // (PlacePanels converts to DIPs), matching the unit of WindowBounds.
        var source = PresentationSource.FromVisual(this);
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var corner = toDevice.Transform(new Point(SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight));
        return new ForegroundProcess.WindowBounds(0, 0, (int)Math.Round(corner.X), (int)Math.Round(corner.Y));
    }

    private void RefreshResults()
    {
        ResultsList.ItemsSource = (_kind == DatabaseSearchKind.Monsters
            ? _database.SearchMonsters(SearchBox.Text, ReadMonsterFilter())
            : _database.Search(_kind, SearchBox.Text)).ToList();
        FooterText.Text = T($"{ResultsList.Items.Count} результатов · максимум 100", $"{ResultsList.Items.Count} results · maximum 100");
        if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = 0;
        else _details.Clear();
    }

    private async void RagnarokPlusSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ServerProfile != GameServerProfile.RagnarokPlus) return;
        try
        {
            var browser = _ragnarokPlusMarket ??= new();
            await browser.SignInAsync();
            browser.IsVisibleChanged -= SignInVisibilityChanged;
            browser.IsVisibleChanged += SignInVisibilityChanged;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"RAGNAROK+ SIGN-IN WINDOW ERROR: {ex.GetType().Name}");
            FooterText.Text = T("Не удалось открыть вход Ragnarok+. Проверьте наличие WebView2 Runtime.",
                "Could not open Ragnarok+ sign-in. Check that WebView2 Runtime is installed.");
        }
    }

    private async void SignInVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue) return;
        ((RagnarokPlusMarketWindow)sender).IsVisibleChanged -= SignInVisibilityChanged;
        _nextCatalogAttempt = default;
        await RefreshMarketInBackgroundAsync();
    }

    private async Task RefreshMarketResultsAsync(DatabaseItem? selectedItem = null)
    {
        if (!GameServerProfile.SupportsMarket(_settings.ServerProfile)) return;
        var query = SearchBox.Text.Trim();
        _marketSearchCancellation?.Cancel();
        _marketSearchCancellation = null;
        var searchVersion = ++_marketSearchVersion;

        ResultsList.ItemsSource = null;
        _details.Clear();
        if (query.Length == 0)
        {
            FooterText.Text = T("Введите название предмета и нажмите «Найти».",
                "Enter an item name and press Search.");
            SearchButton.IsEnabled = true;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _marketSearchCancellation = cancellation;
        SearchButton.IsEnabled = false;
        var ragnarokPlus = _settings.ServerProfile == GameServerProfile.RagnarokPlus;
        FooterText.Text = T($"Ищем «{query}» в маркете…", $"Searching the market for “{query}”…");
        try
        {
            var type = MarketBuyMode.IsChecked == true ? "BUY" : "SELL";
            IReadOnlyList<MarketListing> listings;
            if (ragnarokPlus)
            {
                DatabaseItem[] candidates;
                if (selectedItem is not null)
                    candidates = [selectedItem];
                else if (int.TryParse(query.TrimStart('#'), out var id) && id > 0)
                {
                    var itemById = _database.Item(id);
                    candidates = itemById is null
                        ? await (_ragnarokPlusMarket ??= new()).FindItemsAsync(query, cancellation.Token)
                        : [itemById];
                    candidates = candidates.Where(item => item.Id == id).ToArray();
                }
                else
                {
                    candidates = _database.Search(DatabaseSearchKind.Items, query).Select(result => (DatabaseItem)result.Value).ToArray();
                    if (!_catalogComplete)
                    {
                        var liveItems = await (_ragnarokPlusMarket ??= new()).FindItemsAsync(query, cancellation.Token);
                        candidates = candidates.Concat(liveItems).DistinctBy(item => item.Id)
                            .OrderByDescending(item => item.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
                    }
                }
                cancellation.Token.ThrowIfCancellationRequested();
                var exact = candidates.Where(item => item.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
                if (exact.Length == 1) candidates = exact;
                if (candidates.Length != 1)
                {
                    FooterText.Text = candidates.Length == 0
                        ? T("Предмет не найден в каталоге Ragnarok+.", "Item not found in the Ragnarok+ catalog.")
                        : T("Выберите предмет из списка.", "Select an item from the list.");
                    ResultsList.ItemsSource = candidates.Take(100).Select(item =>
                        new DatabaseSearchResult(DatabaseSearchKind.Items, item, item.Name, $"#{item.Id}")).ToList();
                    return;
                }

                var item = candidates[0];
                listings = await (_ragnarokPlusMarket ??= new()).SearchAsync(item, type, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (type == "SELL") RecordRagnarokPlusOffers(item.Id, listings);
            }
            else
                listings = await _market.SearchAsync(query, type, cancellation.Token);
            if (searchVersion != _marketSearchVersion || cancellation.IsCancellationRequested) return;
            ResultsList.ItemsSource = listings.Select(x => new DatabaseSearchResult(DatabaseSearchKind.Market, x,
                $"{x.ItemName} · {x.Price}" + (ragnarokPlus ? $" · {x.GameMode}" : ""),
                T($"{x.Quantity} шт. · {x.Owner} · {x.Map} ({x.X},{x.Y})",
                    $"{x.Quantity} pcs · {x.Owner} · {x.Map} ({x.X},{x.Y})"))).ToList();
            FooterText.Text = ragnarokPlus
                ? (listings.Count == 0
                    ? T("Нет действующих предложений.", "No active listings.")
                    : T($"{listings.Count} действующих предложений", $"{listings.Count} active listings"))
                : listings.Count == 0
                ? (_market.HasSnapshot
                    ? T($"По названию «{query}» предложений не найдено.", $"No listings found for “{query}”.")
                    : T("Маркет обновляется в фоне. Результаты появятся автоматически.",
                        "The market is refreshing in the background. Results will appear automatically."))
                : T($"{listings.Count} предложений · результат из локальной базы",
                    $"{listings.Count} listings · result from the local database");
            if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = 0;
            else _details.Clear();
        }
        catch (OperationCanceledException) { }
        catch (RagnarokPlusSignInRequiredException)
        {
            if (searchVersion != _marketSearchVersion) return;
            FooterText.Text = T("Войдите на сайте Ragnarok+ в открывшемся окне и повторите поиск.",
                "Sign in on Ragnarok+ in the opened window and repeat your search.");
        }
        catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            if (searchVersion != _marketSearchVersion) return;
            FooterText.Text = T("Для поиска Ragnarok+ требуется WebView2 Runtime.",
                "Ragnarok+ search requires WebView2 Runtime.");
        }
        catch (Exception ex)
        {
            if (searchVersion != _marketSearchVersion) return;
            DebugLog.Write($"MARKET LOAD ERROR: {ex}");
            ResultsList.ItemsSource = null;
            _details.Clear();
            FooterText.Text = T("Не удалось загрузить маркет. Проверьте интернет-соединение.",
                "Could not load the market. Check your internet connection.");
        }
        finally
        {
            if (searchVersion == _marketSearchVersion) SearchButton.IsEnabled = true;
            if (ReferenceEquals(_marketSearchCancellation, cancellation)) _marketSearchCancellation = null;
            cancellation.Dispose();
        }
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out DatabaseSearchKind kind))
        {
            var previousKind = _kind;
            _kind = kind;
            // Checked fires while InitializeComponent is still constructing the visual tree.
            // No named controls other than the sender are guaranteed to exist at that point.
            if (!IsLoaded) return;
            _searchQueries[previousKind] = SearchBox.Text;
            _restoringSearchQuery = true;
            SearchBox.Text = _searchQueries.GetValueOrDefault(kind, "");
            SearchBox.CaretIndex = SearchBox.Text.Length;
            _restoringSearchQuery = false;
            if (MarketTypePanel is not null) MarketTypePanel.Visibility = kind == DatabaseSearchKind.Market ? Visibility.Visible : Visibility.Collapsed;
            if (MonsterAdvancedHost is not null) MonsterAdvancedHost.Visibility = kind == DatabaseSearchKind.Monsters ? Visibility.Visible : Visibility.Collapsed;
            SearchBox.ToolTip = kind == DatabaseSearchKind.Market
                ? T("Поиск по названию или ID предмета", "Search by item name or ID")
                : T("Поиск по названию или ID", "Search by name or ID");
            if (kind != DatabaseSearchKind.Market)
            {
                _marketSearchCancellation?.Cancel();
                _marketSearchCancellation = null;
                _marketSearchVersion++;
                SearchButton.IsEnabled = true;
            }
            if (kind == DatabaseSearchKind.Market)
            {
                ResultsList.ItemsSource = null;
                _details.Clear();
                FooterText.Text = T("Введите название предмета и нажмите «Найти».",
                    "Enter an item name and press Search.");
            }
            else RefreshResults();
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _restoringSearchQuery) return;
        _searchQueries[_kind] = SearchBox.Text;
        if (_kind != DatabaseSearchKind.Market) RefreshResults();
    }
    private async void Search_Click(object sender, RoutedEventArgs e) => await SubmitSearchAsync();
    private async void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SubmitSearchAsync();
    }

    private async Task SubmitSearchAsync()
    {
        if (_kind == DatabaseSearchKind.Market)
        {
            await RefreshMarketResultsAsync();
            return;
        }
        RefreshResults();
    }
    private async void MarketType_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && MarketSellMode is not null && MarketBuyMode is not null && _kind == DatabaseSearchKind.Market && !string.IsNullOrWhiteSpace(SearchBox.Text))
            await RefreshMarketResultsAsync();
    }
    private async void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not DatabaseSearchResult result) return;
        if (_kind == DatabaseSearchKind.Market && _settings.ServerProfile == GameServerProfile.RagnarokPlus &&
            result.Value is DatabaseItem item)
        {
            SearchBox.Text = $"#{item.Id}";
            await RefreshMarketResultsAsync(item);
            return;
        }
        _details.ShowDetails(result.Value);
    }
    private void AdvancedFilter_Changed(object sender, RoutedEventArgs e)
    { if (IsLoaded && _kind == DatabaseSearchKind.Monsters) RefreshResults(); }
    private void AdvancedFilter_Changed(object sender, TextChangedEventArgs e)
    { if (IsLoaded && _kind == DatabaseSearchKind.Monsters) RefreshResults(); }

    private MonsterSearchFilter ReadMonsterFilter() => new()
    {
        Race = RaceFilter.SelectedIndex > 0 ? RaceFilter.SelectedItem?.ToString() ?? "" : "",
        Element = ElementFilter.SelectedIndex > 0 ? ElementFilter.SelectedItem?.ToString() ?? "" : "",
        Size = SizeFilter.SelectedIndex > 0 ? SizeFilter.SelectedItem?.ToString() ?? "" : "",
        LevelMin = Number(LevelMinFilter.Text), LevelMax = Number(LevelMaxFilter.Text),
        BaseExpMin = Number(BaseExpMinFilter.Text), JobExpMin = Number(JobExpMinFilter.Text),
        HpMin = Number(HpMinFilter.Text), HpMax = Number(HpMaxFilter.Text),
        ElementLevelMin = Number(ElementLevelMinFilter.Text),
        AggressiveOnly = AggressiveFilter.IsChecked == true,
        BossOnly = BossFilter.IsChecked == true, MvpOnly = MvpFilter.IsChecked == true
    };

    private static int Number(string? value) => int.TryParse(value, out var result) ? Math.Max(0, result) : 0;

    private TimeSpan MarketRefreshInterval() =>
        TimeSpan.FromMinutes(Math.Clamp(_settings.MarketBackgroundRefreshMinutes, 1, 1440));

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.MarketBackgroundRefreshMinutes))
            _marketRefreshTimer.Interval = MarketRefreshInterval();
        if (e.PropertyName == nameof(AppSettings.ServerProfile))
        {
            if (_settings.ServerProfile != GameServerProfile.RagnarokPlus) _catalogSync?.Cancel();
            else
            {
                _nextCatalogAttempt = default;
                Dispatcher.BeginInvoke(async () => await RefreshMarketInBackgroundAsync(), DispatcherPriority.Background);
            }
        }
    }

    private async Task RefreshMarketInBackgroundAsync()
    {
        if (_marketRefreshRunning || !GameServerProfile.SupportsMarketWatch(_settings.ServerProfile)) return;
        _marketRefreshRunning = true;
        try
        {
            if (_settings.ServerProfile == GameServerProfile.RagnarokPlus)
            {
                _ = SynchronizeRagnarokPlusCatalogAsync();
                await RefreshRagnarokPlusOffersAsync();
            }
            else
            {
                await _market.RefreshSnapshotAsync();
                await _market.RefreshKnownQueriesAsync();
            }
        }
        finally { _marketRefreshRunning = false; }
    }

    private async Task SynchronizeRagnarokPlusCatalogAsync()
    {
        if (_catalogDetailsComplete || _catalogSync is not null || _nextCatalogAttempt > DateTimeOffset.UtcNow ||
            (_ragnarokPlusMarket is { IsVisible: true, ShowInTaskbar: true })) return;
        _nextCatalogAttempt = DateTimeOffset.UtcNow.AddMinutes(10);
        using var cancellation = new CancellationTokenSource();
        _catalogSync = cancellation;
        try
        {
            var catalog = RagnarokPlusData.LoadCatalog();
            if (!catalog.CatalogSynchronizedUtc.HasValue)
            {
                if (IsDatabaseOpen && (_kind is DatabaseSearchKind.Items or DatabaseSearchKind.Monsters))
                    FooterText.Text = T("Загружаем каталог Ragnarok+…", "Downloading the Ragnarok+ catalog…");
                catalog = await (_ragnarokPlusCatalog ??= new()).ReadCatalogAsync(cancellation.Token);
                PublishCatalog(catalog);
            }
            _catalogComplete = true;
            if (catalog.Items.All(item => item.RagnarokPlusDetailsCaptured))
            {
                _catalogDetailsComplete = true;
                return;
            }
            await (_ragnarokPlusCatalog ??= new()).EnrichItemsAsync(catalog, PublishCatalog, cancellation.Token);
            _catalogDetailsComplete = catalog.Items.All(item => item.RagnarokPlusDetailsCaptured);
        }
        catch (RagnarokPlusSignInRequiredException)
        {
            if (IsDatabaseOpen && (_kind is DatabaseSearchKind.Items or DatabaseSearchKind.Monsters))
                FooterText.Text = T("Войдите в Ragnarok+, чтобы загрузить каталог.", "Sign in to Ragnarok+ to download the catalog.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            DebugLog.Write($"RAGNAROK+ CATALOG ERROR: {ex.Message}");
            if (IsDatabaseOpen && (_kind is DatabaseSearchKind.Items or DatabaseSearchKind.Monsters))
                FooterText.Text = T("Каталог не загрузился. RoAura повторит попытку.", "Catalog download failed. RoAura will retry.");
        }
        finally { _catalogSync = null; }
    }

    private void PublishCatalog(GameDatabase catalog)
    {
        RagnarokPlusData.SaveCatalog(catalog);
        if (_settings.ServerProfile != GameServerProfile.RagnarokPlus) return;
        _database.Reload(GameServerProfile.RagnarokPlus);
        (Application.Current as App)?.RagnarokPlusCatalogUpdated();
        if (IsDatabaseOpen && (_kind is DatabaseSearchKind.Items or DatabaseSearchKind.Monsters))
        {
            var previous = (ResultsList.SelectedItem as DatabaseSearchResult)?.Value;
            RefreshResults();
            var selected = ResultsList.Items.OfType<DatabaseSearchResult>().FirstOrDefault(result =>
                (previous, result.Value) switch
                {
                    (DatabaseItem before, DatabaseItem after) => before.Id == after.Id,
                    (DatabaseMonster before, DatabaseMonster after) => before.Id == after.Id,
                    _ => false
                });
            if (selected is not null) ResultsList.SelectedItem = selected;
        }
        DebugLog.Write($"RAGNAROK+ CATALOG items={catalog.Items.Count}; details={catalog.Items.Count(item => item.RagnarokPlusDetailsCaptured)}; monsters={catalog.Monsters.Count}");
    }

    private async Task RefreshRagnarokPlusOffersAsync()
    {
        var items = _settings.RagnarokPlusMarketWatchRules.Where(rule => rule.Enabled)
            .Select(rule => new DatabaseItem { Id = rule.ItemId, Name = rule.ItemName })
            .Concat(_settings.RagnarokPlusGoldGoblinPriceRules.Where(rule => rule.UseVendingPrice && !rule.Exclude)
                .Select(rule => rule.ItemId > 0
                    ? _database.Item(rule.ItemId) ?? new DatabaseItem { Id = rule.ItemId, Name = rule.Name }
                    : _database.Items.FirstOrDefault(item => item.Name.Equals(rule.Name, StringComparison.CurrentCultureIgnoreCase)))
                .OfType<DatabaseItem>())
            .Where(item => item.Id > 0 && !string.IsNullOrWhiteSpace(item.Name))
            .DistinctBy(item => item.Id).ToArray();

        foreach (var item in items)
        {
            if (_settings.ServerProfile != GameServerProfile.RagnarokPlus || Dispatcher.HasShutdownStarted) break;
            try
            {
                var browser = _ragnarokPlusMarket ??= new();
                if (browser.IsVisible && browser.ShowInTaskbar) break;
                var offers = await browser.SearchAsync(item, "SELL", CancellationToken.None, interactive: false);
                if (_settings.ServerProfile != GameServerProfile.RagnarokPlus) break;
                RecordRagnarokPlusOffers(item.Id, offers);
            }
            catch (RagnarokPlusSignInRequiredException) { break; }
            catch (Exception ex)
            {
                DebugLog.Write($"RAGNAROK+ WATCH item={item.Id}: {ex.GetType().Name}");
            }
            await Task.Delay(1000);
        }
    }

    private static void RecordRagnarokPlusOffers(int itemId, IReadOnlyList<MarketListing> offers)
    {
        try { RagnarokPlusData.RememberLiveOffers(itemId, offers); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            DebugLog.Write($"RAGNAROK+ PRICE CACHE ERROR: {ex.GetType().Name}");
        }
        (Application.Current as App)?.EvaluateRagnarokPlusOffers(itemId, offers);
    }

    private void Market_DataUpdated() => Dispatcher.BeginInvoke(async () =>
    {
        if (_settings.ServerProfile == GameServerProfile.SpringRo && IsDatabaseOpen && _kind == DatabaseSearchKind.Market && !string.IsNullOrWhiteSpace(SearchBox.Text))
            await RefreshMarketResultsAsync();
    });
    private string T(string ru, string en) => _settings.Language == "en" ? en : ru;
    private void Close_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke();

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
