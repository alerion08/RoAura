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
    private bool _gameWasForeground;
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

    public void RefreshLanguage()
    {
        var raceIndex = Math.Max(0, RaceFilter.SelectedIndex);
        var elementIndex = Math.Max(0, ElementFilter.SelectedIndex);
        var sizeIndex = Math.Max(0, SizeFilter.SelectedIndex);
        OverlayTitle.Text = T("База SpringRO", "SpringRO Database");
        MonstersMode.Content = T("Монстры", "Monsters");
        ItemsMode.Content = T("Предметы", "Items");
        MapsMode.Content = T("Карты", "Maps");
        MarketMode.Content = T("Маркет", "Market");
        MarketSellMode.Content = T("Продажа", "Selling");
        MarketBuyMode.Content = T("Покупка", "Buying");
        SearchButton.Content = T("Найти", "Search");
        MarketTypeLabel.Text = T("Тип магазина:", "Shop type:");
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
        _gameWasForeground = false;
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
        var databaseHasFocus = foreground == searchHandle || foreground == detailHandle;

        if (ForegroundProcess.TryGetForegroundAllowedBounds(_settings.AllowedProcesses, out var game))
        {
            _lastRelevantForegroundUtc = DateTime.UtcNow;
            if (!game.Equals(_gameBounds))
            {
                _gameBounds = game;
                PlacePanels(game);
            }
            SetPanelsVisible(true);
            // A fullscreen/borderless RO client can put its own topmost window back at
            // the head of the topmost band on the first click.  Calling HWND_TOPMOST
            // again on a window that is already topmost is not sufficient on every
            // Windows build.  Reinsert both panels into the band without activation
            // when the game becomes foreground; afterwards only fix the z-order if
            // it was released.
            if (!_gameWasForeground) RestorePanelsAfterGameActivation();
            else if (!_isTopmost) ForceTopmost();
            _gameWasForeground = true;
        }
        else if (databaseHasFocus)
        {
            _gameWasForeground = false;
            _lastRelevantForegroundUtc = DateTime.UtcNow;
            SetPanelsVisible(true);
            if (!_isTopmost) ForceTopmost();
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

    private void RestorePanelsAfterGameActivation()
    {
        var searchHandle = new WindowInteropHelper(this).Handle;
        var detailHandle = _details.Handle;
        if (searchHandle == IntPtr.Zero || detailHandle == IntPtr.Zero) return;

        // Reinsert without Hide/Show. Hiding the HWND here caused the database to
        // disappear on the first mouse click in fullscreen clients.
        PromotePanelsAboveGame();
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

    private async Task RefreshMarketResultsAsync()
    {
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
        FooterText.Text = T($"Ищем «{query}» в актуальном маркете SpringRO…",
            $"Searching the live SpringRO market for “{query}”…");
        try
        {
            var type = MarketBuyMode.IsChecked == true ? "BUY" : "SELL";
            var listings = await _market.SearchAsync(query, type, cancellation.Token);
            if (searchVersion != _marketSearchVersion || cancellation.IsCancellationRequested) return;
            ResultsList.ItemsSource = listings.Select(x => new DatabaseSearchResult(DatabaseSearchKind.Market, x,
                $"{x.ItemName} · {x.Price}",
                T($"{x.Quantity} шт. · {x.Owner} · {x.Map} ({x.X},{x.Y})",
                    $"{x.Quantity} pcs · {x.Owner} · {x.Map} ({x.X},{x.Y})"))).ToList();
            FooterText.Text = listings.Count == 0
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
                ? T("Поиск только по названию предмета", "Search by item name only")
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
    private async void Search_Click(object sender, RoutedEventArgs e) { if (_kind == DatabaseSearchKind.Market) await RefreshMarketResultsAsync(); else RefreshResults(); }
    private async void Search_KeyDown(object sender, KeyEventArgs e) { if (e.Key != Key.Enter) return; if (_kind == DatabaseSearchKind.Market) await RefreshMarketResultsAsync(); else RefreshResults(); }
    private async void MarketType_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && _kind == DatabaseSearchKind.Market && !string.IsNullOrWhiteSpace(SearchBox.Text))
            await RefreshMarketResultsAsync();
    }
    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (ResultsList.SelectedItem is DatabaseSearchResult result) _details.ShowDetails(result.Value); }
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
    }

    private async Task RefreshMarketInBackgroundAsync()
    {
        if (_marketRefreshRunning) return;
        _marketRefreshRunning = true;
        try
        {
            await _market.RefreshSnapshotAsync();
            await _market.RefreshKnownQueriesAsync();
        }
        finally { _marketRefreshRunning = false; }
    }

    private void Market_DataUpdated() => Dispatcher.BeginInvoke(async () =>
    {
        if (IsDatabaseOpen && _kind == DatabaseSearchKind.Market && !string.IsNullOrWhiteSpace(SearchBox.Text))
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
