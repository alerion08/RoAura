using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

internal sealed class RagnarokPlusMarketWindow : Window
{
    private const string Host = "ragnarok.plus";
    private static readonly Uri Home = new($"https://{Host}/");
    private readonly WebView2 _browser = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private bool _initialized;

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
            Show();
            Activate();
            await InitializeAsync();
            _browser.CoreWebView2.Navigate(new Uri(Home, "login").AbsoluteUri);
        }
        finally { _operation.Release(); }
    }

    public async Task<DatabaseItem[]> FindItemsAsync(string query, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            await InitializeForSearchAsync();
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
                    return items.DistinctBy(item => item.Id).ToArray();
                await Task.Delay(250, cancellationToken);
            }
            return [];
        }
        finally { _operation.Release(); }
    }

    private async Task InitializeForSearchAsync()
    {
        if (_initialized || IsVisible)
        {
            await InitializeAsync();
            return;
        }
        Show();
        try { await InitializeAsync(); }
        finally { Hide(); }
    }

    private void EnsurePage(string path)
    {
        if (Uri.TryCreate(_browser.CoreWebView2.Source, UriKind.Absolute, out var source) && IsRagnarokPlus(source))
        {
            if (source.AbsolutePath == path) return;
            if (source.AbsolutePath == "/login")
            {
                Show();
                Activate();
                throw new RagnarokPlusSignInRequiredException();
            }
        }
        throw new InvalidDataException("Unexpected Ragnarok+ page.");
    }

    public async Task<IReadOnlyList<MarketListing>> SearchAsync(DatabaseItem item, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        var wasVisible = IsVisible;
        var keepOpen = false;
        try
        {
            // Keep the browser rendered while the site loads its interactive market dialog.
            if (!IsVisible) Show();
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
                    EnsurePage(itemPath);

                    var json = await _browser.CoreWebView2.ExecuteScriptAsync(MarketScript(itemPath, DateTimeOffset.UtcNow < nextOpenAttempt)).WaitAsync(token);
                    var state = JsonSerializer.Deserialize<MarketPageState>(json);
                    if (state?.Stage is { } stage && stage != lastStage)
                    {
                        DebugLog.Write($"RAGNAROK+ MARKET item={item.Id}; stage={stage}");
                        lastStage = stage;
                    }
                    if (state?.Rows is { } rows)
                        return rows.Select(row => row.ToListing(item)).ToArray();
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
            keepOpen = true;
            throw;
        }
        finally
        {
            if (!wasVisible && !keepOpen && IsVisible) Hide();
            _operation.Release();
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

    private static string MarketScript(string path, bool opened) => $$"""
        (() => {
            if (location.origin !== 'https://ragnarok.plus' || location.pathname !== {{JsonSerializer.Serialize(path)}})
                return null;
            const dialog = [...document.querySelectorAll('[role="dialog"], dialog')]
                .find(node => node.getClientRects().length && node.innerText.includes('Live Market'));
            if (dialog) {
                const summary = [...dialog.querySelectorAll('p')].map(node => node.textContent.trim())
                    .find(text => /\b[\d,]+\s+selling\b/i.test(text));
                const block = [...dialog.querySelectorAll('.stmod__table')]
                    .find(table => {
                        const section = table.parentElement;
                        return [...section.querySelectorAll('*')].some(node =>
                            node.childElementCount === 0 && /^Selling$/i.test(node.textContent.trim()));
                    });
                const rows = [...(block ?? dialog).querySelectorAll('.stmod__row')].map(row => {
                    const text = selector => row.querySelector(selector)?.textContent.trim() ?? '';
                    const location = /([A-Za-z0-9_-]+)\s*\(\s*(\d+)\s*,\s*(\d+)\s*\)/.exec(text('.stmod__stall-loc'));
                    const digits = selector => text(selector).replace(/[^0-9]/g, '');
                    return {
                        Owner: text('.stmod__stall-name'), Stall: text('.stmod__stall-title'),
                        GameMode: text('.mode-badge').toUpperCase(),
                        Map: location?.[1] ?? '', X: Number(location?.[2] ?? 0), Y: Number(location?.[3] ?? 0),
                        Quantity: digits('.stmod__qty'), Price: digits('.stmod__price')
                    };
                });
                const count = /([\d,]+)\s+selling/i.exec(summary ?? '')?.[1];
                if (count !== undefined && rows.length === Number(count.replaceAll(',', '')) &&
                    rows.every(row => row.Price && row.Quantity && row.Owner)) {
                    return { Rows: rows, Stage: 'rows-read' };
                }
                if (summary && !rows.length && /^0 selling/i.test(summary))
                    return { NoOffers: true, Stage: 'no-offers' };
                return { Stage: 'dialog: rows=' + rows.length + '; expected=' + (count ?? '?') +
                    '; complete=' + rows.filter(row => row.Price && row.Quantity && row.Owner).length };
            }
            if ({{(opened ? "true" : "false")}}) return { Stage: 'waiting-for-dialog' };
            const button = [...document.querySelectorAll('button')]
                .find(node => node.getClientRects().length && /^View\s+[\d,]+\s+shops?$/i.test(node.innerText.trim()));
            if (button) {
                button.click();
                return { Opened: true, Stage: 'shop-button-clicked' };
            }
            const lines = document.body.innerText.split('\n').map(value => value.trim());
            return { Stage: 'waiting-for-shop-button', NoOffers: lines.some(value => /^market$/i.test(value)) &&
                lines.some(value => /^(No (open shops|active listings|active offers|market data)|0 open shops)\.?$/i.test(value)) };
        })()
        """;

    private sealed record MarketPageState(LiveMarketRow[]? Rows, bool NoOffers, bool Opened, string? Stage);

    private sealed record LiveMarketRow(string Owner, string Stall, string GameMode,
        string Map, int X, int Y, string Quantity, string Price)
    {
        public MarketListing ToListing(DatabaseItem item) => new()
        {
            ItemId = item.Id, ItemName = item.Name, ShopType = "SELL", Owner = Owner,
            Stall = Stall, GameMode = GameMode, Map = Map, X = X, Y = Y,
            Quantity = Quantity, Price = Price
        };
    }
}

internal sealed class RagnarokPlusSignInRequiredException : Exception { }
