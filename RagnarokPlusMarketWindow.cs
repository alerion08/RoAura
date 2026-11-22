using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

internal sealed partial class RagnarokPlusMarketWindow : Window
{
    private const string Host = "ragnarok.plus";
    private static readonly Uri Home = new($"https://{Host}/");
    private readonly WebView2 _browser = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private bool _initialized;
    private static readonly System.Net.Http.HttpClient MapClient = new() { Timeout = TimeSpan.FromSeconds(4) };

    public RagnarokPlusMarketWindow()
    {
        Title = "Ragnarok+";
        Width = 1000;
        Height = 760;
        Content = _browser;
        ShowInTaskbar = true;
        Closing += (_, e) =>
        {
            if (Dispatcher.HasShutdownStarted || Application.Current?.MainWindow is null)
                return;
            e.Cancel = true;
            Hide();
        };
        Closed += (_, _) => _browser.Dispose();
    }

    public async Task SignInAsync()
    {
        await _operation.WaitAsync();
        try
        {
            ShowInteractive();
            await InitializeAsync();
            _browser.CoreWebView2.Navigate(new Uri(Home, "login").AbsoluteUri);
        }
        finally { _operation.Release(); }
    }

    public async Task<DatabaseItem[]> FindItemsAsync(string query, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        var wasVisible = IsVisible;
        var keepOpen = false;
        try
        {
            if (!IsVisible) ShowBackground();
            await InitializeAsync();
            await NavigateAsync(new Uri(Home, "item-db?q=" + Uri.EscapeDataString(query.TrimStart('#'))),
                cancellationToken);
            for (var attempt = 0; attempt < 40; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsurePage("/item-db");
                const string script = """
                    (() => {
                        if (location.origin !== 'https://ragnarok.plus' || location.pathname !== '/item-db')
                            return null;
                        const links = [...document.querySelectorAll('a[href^="/item-db/"]')]
                            .filter(node => node.getClientRects().length);
                        const items = links.flatMap(node => {
                            const match = /^\/item-db\/(\d+)$/.exec(new URL(node.href).pathname);
                            const lines = node.innerText.split(/\n/).map(value => value.trim()).filter(Boolean);
                            return match && lines.length >= 2
                                ? [{ Id: Number(match[1]), Name: lines[0], Type: lines[1] }]
                                : [];
                        });
                        return items.length ? items : null;
                    })()
                    """;
                var json = await _browser.CoreWebView2.ExecuteScriptAsync(script).WaitAsync(cancellationToken);
                if (JsonSerializer.Deserialize<DatabaseItem[]>(json) is { Length: > 0 } items)
                {
                    var matches = int.TryParse(query.TrimStart('#'), out var id)
                        ? items.Where(item => item.Id == id)
                        : items.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
                    return matches.DistinctBy(item => item.Id).ToArray();
                }
                await Task.Delay(250, cancellationToken);
            }
            return [];
        }
        catch (RagnarokPlusSignInRequiredException)
        {
            keepOpen = true;
            throw;
        }
        finally
        {
            if (!wasVisible && !keepOpen && IsVisible) Hide();
            _operation.Release();
        }
    }

    private void ShowBackground()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
        ShowInTaskbar = false;
        Left = SystemParameters.VirtualScreenLeft - Width - 100;
        Top = SystemParameters.VirtualScreenTop;
        Show();
    }

    private void ShowInteractive()
    {
        if (IsVisible) Hide();
        Left = SystemParameters.WorkArea.Left + Math.Max(0, (SystemParameters.WorkArea.Width - Width) / 2);
        Top = SystemParameters.WorkArea.Top + Math.Max(0, (SystemParameters.WorkArea.Height - Height) / 2);
        ShowInTaskbar = true;
        ShowActivated = true;
        Show();
        Activate();
    }

    private void EnsurePage(string path, bool interactive = true)
    {
        if (Uri.TryCreate(_browser.CoreWebView2.Source, UriKind.Absolute, out var source) && IsRagnarokPlus(source))
        {
            if (source.AbsolutePath.TrimEnd('/') == path.TrimEnd('/')) return;
            if (source.AbsolutePath == "/login")
            {
                if (interactive) ShowInteractive();
                throw new RagnarokPlusSignInRequiredException();
            }
        }
        throw new InvalidDataException("Unexpected Ragnarok+ page.");
    }

    public async Task<IReadOnlyList<MarketListing>> SearchAsync(DatabaseItem item, string shopType, CancellationToken cancellationToken, bool interactive = true)
    {
        await _operation.WaitAsync(cancellationToken);
        var wasVisible = IsVisible;
        var keepOpen = false;
        try
        {
            // Keep the browser rendered while the site loads its interactive market dialog.
            if (!IsVisible) ShowBackground();
            await InitializeAsync();
            var itemPath = $"/item-db/{item.Id}";
            await NavigateAsync(new Uri(Home, itemPath), cancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var token = deadline.Token;
            var nextOpenAttempt = DateTimeOffset.MinValue;
            string? lastStage = null;
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    EnsurePage(itemPath, interactive);

                    var json = await _browser.CoreWebView2.ExecuteScriptAsync(MarketScript(itemPath, shopType, DateTimeOffset.UtcNow < nextOpenAttempt)).WaitAsync(token);
                    var state = JsonSerializer.Deserialize<MarketPageState>(json);
                    if (state?.Stage is { } stage && stage != lastStage)
                    {
                        DebugLog.Write($"RAGNAROK+ MARKET item={item.Id}; stage={stage}");
                        lastStage = stage;
                    }
                    if (state?.Rows is { } rows)
                    {
                        var listings = rows.Select(row => row.ToListing(item, shopType)).ToArray();
                        if (interactive) await LoadMapImagesAsync(itemPath, listings, cancellationToken);
                        return listings;
                    }
                    if (state?.NoOffers == true) return [];
                    if (state?.Opened == true) nextOpenAttempt = DateTimeOffset.UtcNow.AddSeconds(2);
                    await Task.Delay(250, token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Ragnarok+ Live Market timed out at {lastStage ?? "no page state"}.");
            }
        }
        catch (RagnarokPlusSignInRequiredException)
        {
            keepOpen = interactive;
            throw;
        }
        finally
        {
            if (!wasVisible && !keepOpen && IsVisible) Hide();
            _operation.Release();
        }
    }

    private async Task LoadMapImagesAsync(string itemPath, MarketListing[] listings, CancellationToken token)
    {
        foreach (var group in listings.Where(row => row.MapPinLeft.HasValue && row.MapPinTop.HasValue)
                     .GroupBy(row => row.Map))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(group.Key, @"^[A-Za-z0-9_-]+$")) continue;
            try
            {
                var url = $"https://cdn.ragnarok.plus/images/maps/{group.Key}.webp";
                var webp = await MapClient.GetByteArrayAsync(url, token);
                EnsurePage(itemPath);
                var script = $$"""
                    (async () => {
                        const bytes = Uint8Array.from(atob({{JsonSerializer.Serialize(Convert.ToBase64String(webp))}}),
                            value => value.charCodeAt(0));
                        const image = await createImageBitmap(new Blob([bytes], {type: 'image/webp'}));
                        const canvas = document.createElement('canvas');
                        canvas.width = image.width;
                        canvas.height = image.height;
                        canvas.getContext('2d').drawImage(image, 0, 0);
                        image.close();
                        return canvas.toDataURL('image/png').split(',')[1];
                    })()
                    """;
                // ExecuteScriptAsync does not await a Promise. CDP returns only its resolved value.
                var parameters = JsonSerializer.Serialize(new { expression = script, awaitPromise = true, returnByValue = true });
                var result = await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters).WaitAsync(token);
                using var json = JsonDocument.Parse(result);
                if (json.RootElement.TryGetProperty("result", out var value) &&
                    value.TryGetProperty("value", out var png) && png.GetString() is { Length: > 0 } image)
                {
                    var bytes = Convert.FromBase64String(image);
                    foreach (var row in group) row.MapImagePng = bytes;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            {
                DebugLog.Write($"RAGNAROK+ MARKET MAP ERROR: {ex.GetType().Name}");
            }
        }
    }

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        var directory = Path.Combine(AppEnvironment.LocalDirectory, "RagnarokPlusWebView");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: directory);
        await _browser.EnsureCoreWebView2Async(environment);
        var core = _browser.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !IsRagnarokPlus(uri))
                e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        _initialized = true;
    }

    private async Task NavigateAsync(Uri uri, CancellationToken cancellationToken)
    {
        var core = _browser.CoreWebView2;
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) => completed.TrySetResult(e.IsSuccess);
        core.NavigationCompleted += OnCompleted;
        try
        {
            core.Navigate(uri.AbsoluteUri);
            if (!await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken))
                throw new IOException("Ragnarok+ item page navigation failed.");
        }
        finally { core.NavigationCompleted -= OnCompleted; }
    }

    private static bool IsRagnarokPlus(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.Host == Host;

    private static string MarketScript(string path, string shopType, bool opened) => $$"""
        (() => {
            if (location.origin !== 'https://ragnarok.plus' || location.pathname !== {{JsonSerializer.Serialize(path)}})
                return null;
            const dialog = [...document.querySelectorAll('[role="dialog"], dialog')]
                .find(node => node.getClientRects().length && node.innerText.includes('Live Market'));
            if (dialog) {
                const side = {{JsonSerializer.Serialize(shopType == "BUY" ? "buying" : "selling")}};
                const counts = [...dialog.querySelectorAll('p')].flatMap(node =>
                    [...node.textContent.matchAll(/([\d,]+)\s+(selling|buying)\b/gi)]);
                const count = counts.find(match => match[2].toLowerCase() === side)?.[1];
                const blocks = [...dialog.querySelectorAll('.stmod__table')];
                const headings = [...dialog.querySelectorAll('*')].filter(node =>
                    node.childElementCount === 0 && !node.closest('.stmod__table') && /^(selling|buying)$/i.test(node.textContent.trim()));
                const block = blocks.find(table => {
                    const header = [...table.querySelectorAll('*')].find(node =>
                        node.childElementCount === 0 && /^buying shop$/i.test(node.textContent.trim()));
                    if (header) return side === 'buying';
                    const heading = headings.filter(node =>
                        node.compareDocumentPosition(table) & Node.DOCUMENT_POSITION_FOLLOWING).at(-1);
                    return heading?.textContent.trim().toLowerCase() === side;
                });
                const pins = [...dialog.querySelectorAll('.db-map-card__pin')];
                if ((count === '0' || count === undefined) && counts.length && !block)
                    return { NoOffers: true, Stage: 'no-offers-for-side' };
                const rows = [...(block ?? dialog.ownerDocument.createElement('div')).querySelectorAll('.stmod__row')].map(row => {
                    const text = selector => row.querySelector(selector)?.textContent.trim() ?? '';
                    const location = /([A-Za-z0-9_-]+)\s*\(\s*(\d+)\s*,\s*(\d+)\s*\)/.exec(text('.stmod__stall-loc'));
                    const digits = selector => text(selector).replace(/[^0-9]/g, '');
                    const owner = text('.stmod__stall-name');
                    const pin = location && pins.find(candidate =>
                        candidate.closest('.db-map-card__wrap')?.querySelector('.db-map-card__img')?.alt === location[1] &&
                        candidate.querySelector('.db-map-card__tooltip-name')?.textContent.trim() === owner &&
                        candidate.querySelector('.db-map-card__tooltip-coords')?.textContent.replace(/\s/g, '') ===
                            location[2] + ',' + location[3]);
                    return {
                        Owner: owner, Stall: text('.stmod__stall-title'),
                        MapPinLeft: pin ? parseFloat(pin.style.left) / 100 : null,
                        MapPinTop: pin ? parseFloat(pin.style.top) / 100 : null,
                        GameMode: text('.mode-badge').toUpperCase(),
                        Map: location?.[1] ?? '', X: Number(location?.[2] ?? 0), Y: Number(location?.[3] ?? 0),
                        Quantity: digits('.stmod__qty'), Price: digits('.stmod__price')
                    };
                });
                if (count !== undefined && rows.length === Number(count.replaceAll(',', '')) &&
                    rows.every(row => row.Price && row.Quantity && row.Owner)) {
                    return { Rows: rows, Stage: 'rows-read' };
                }
                return { Stage: 'dialog: rows=' + rows.length + '; expected=' + (count ?? '?') +
                    '; complete=' + rows.filter(row => row.Price && row.Quantity && row.Owner).length };
            }
            if ({{(opened ? "true" : "false")}}) return { Stage: 'waiting-for-dialog' };
            const button = [...document.querySelectorAll('button')]
                .find(node => node.getClientRects().length && /^View\s+[\d,]+\s+(?:shops?|buy orders?)$/i.test(node.innerText.trim()));
            if (button) {
                button.click();
                return { Opened: true, Stage: 'shop-button-clicked' };
            }
            const lines = document.body.innerText.split('\n').map(value => value.trim());
            return { Stage: 'waiting-for-shop-button', NoOffers: lines.some(value => /^market$/i.test(value)) &&
                lines.some(value => /^(Not currently listed|No (open shops|active listings|active offers|market data)|0 open shops)\.?$/i.test(value)) };
        })()
        """;

    private sealed record MarketPageState(LiveMarketRow[]? Rows, bool NoOffers, bool Opened, string? Stage);

    private sealed record LiveMarketRow(string Owner, string Stall, string GameMode,
        string Map, int X, int Y, string Quantity, string Price, double? MapPinLeft, double? MapPinTop)
    {
        public MarketListing ToListing(DatabaseItem item, string shopType) => new()
        {
            ItemId = item.Id, ItemName = item.Name, ShopType = shopType, Owner = Owner,
            Stall = Stall, GameMode = GameMode, Map = Map, X = X, Y = Y,
            Quantity = Quantity, Price = Price, MapPinLeft = MapPinLeft, MapPinTop = MapPinTop
        };
    }
}

internal sealed class RagnarokPlusSignInRequiredException : Exception { }
