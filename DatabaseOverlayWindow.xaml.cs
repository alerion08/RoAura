using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
    private readonly SpringRoMarketService _market = new();
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
    public bool IsDatabaseOpen { get; private set; }

    public DatabaseOverlayWindow(AppSettings settings, GameDatabaseService database)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        InitializeComponent();
        RaceFilter.ItemsSource = new[] { T("Все расы", "All races") }.Concat(_database.MonsterRaces).ToList();
        ElementFilter.ItemsSource = new[] { T("Все элементы", "All elements") }.Concat(_database.MonsterElements).ToList();
        SizeFilter.ItemsSource = new[] { T("Все размеры", "All sizes") }.Concat(_database.MonsterSizes).ToList();
        RaceFilter.SelectedIndex = ElementFilter.SelectedIndex = SizeFilter.SelectedIndex = 0;
        _details = new DatabaseDetailWindow(settings, database);
        _details.HideRequested += () => HideRequested?.Invoke();
        if (_settings.Language == "en")
        {
            OverlayTitle.Text = "SpringRO Database";
            MonstersMode.Content = "Monsters";
            ItemsMode.Content = "Items";
            MapsMode.Content = "Maps";
            MarketMode.Content = "Market";
            MarketSellMode.Content = "Selling";
            MarketBuyMode.Content = "Buying";
            SearchButton.Content = "Search";
        }
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

    public void PrepareForFirstUse()
    {
        // Create both native windows at application startup. This removes the one-off
        // HWND/DWM initialization race that otherwise occurs on the first database open.
        _ = new WindowInteropHelper(this).EnsureHandle();
        _ = new WindowInteropHelper(_details).EnsureHandle();
        ReleaseTopmost();
    }

    public void ShowForGame()
    {
        var (game, _) = ResolveGameWindow();
        _gameBounds = game;
        PlacePanels(game);

        IsDatabaseOpen = true;
        _temporarilyHidden = false;
        _gameWasForeground = false;
        _lastRelevantForegroundUtc = DateTime.UtcNow;
        Show();
        _details.Show();
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
            // Windows build.  Reinsert both panels into the band without activation.
            if (!_gameWasForeground) RestorePanelsAfterGameActivation();
            else PromotePanelsAboveGame();
            _gameWasForeground = true;
        }
        else if (databaseHasFocus)
        {
            _gameWasForeground = false;
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
        var gameWidth = game.Right - game.Left;
        var gameHeight = game.Bottom - game.Top;
        var margin = Math.Clamp(gameWidth * .008, 6, 14);
        var panelWidth = Math.Clamp(gameWidth * .205, 320, 390);
        var panelHeightMaximum = Math.Max(460, gameHeight - margin * 2);
        var panelHeight = Math.Clamp(gameHeight * .82, 460, panelHeightMaximum);
        Width = panelWidth;
        Height = Math.Min(panelHeight, gameHeight);
        Left = game.Left + margin;
        Top = game.Top + (gameHeight - Height) / 2;
        _details.PlaceForGame(game, panelWidth, Height, margin);
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
        Show();
        _details.Show();
        ShowActivated = true;
        _details.ShowActivated = true;
    }

    private void ForceTopmost()
    {
        SetWindowPos(new WindowInteropHelper(this).Handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        SetWindowPos(_details.Handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private void PromotePanelsAboveGame()
    {
        var searchHandle = new WindowInteropHelper(this).Handle;
        var detailHandle = _details.Handle;
        if (searchHandle == IntPtr.Zero || detailHandle == IntPtr.Zero) return;

        // Deliberately do not assign the Ragnarok HWND as Owner/GWL_HWNDPARENT:
        // doing that made modifier keys stick in protected clients.  A non-activating
        // remove/reinsert is enough to restore the overlay after the game is clicked.
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
        SetWindowPos(new WindowInteropHelper(this).Handle, HwndNotTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        SetWindowPos(_details.Handle, HwndNotTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private (ForegroundProcess.WindowBounds Bounds, IntPtr Handle) ResolveGameWindow()
    {
        if (ForegroundProcess.TryFindWindow(_settings.AllowedProcesses, out var handle, out var game))
            return (game, handle);
        return (new ForegroundProcess.WindowBounds(0, 0,
            (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight), IntPtr.Zero);
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
                $"{x.Quantity} шт. · {x.Owner} · {x.Map} ({x.X},{x.Y})")).ToList();
            FooterText.Text = listings.Count == 0
                ? T($"По названию «{query}» предложений не найдено.", $"No listings found for “{query}”.")
                : T($"{listings.Count} предложений · повторный поиск доступен мгновенно в течение 2 минут",
                    $"{listings.Count} listings · repeat searches are instant for 2 minutes");
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
    private void AdvancedToggle_Changed(object sender, RoutedEventArgs e) =>
        MonsterAdvancedPanel.Visibility = AdvancedToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
