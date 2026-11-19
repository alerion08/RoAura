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

    public async Task<IReadOnlyList<MarketListing>> SearchAsync(DatabaseItem item, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            if (!_initialized && !IsVisible)
            {
                Show();
                try { await InitializeAsync(); }
                finally { Hide(); }
            }
            else
                await InitializeAsync();
            var itemPath = $"/item-db/{item.Id}";
            await NavigateAsync(new Uri(Home, itemPath), cancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var token = deadline.Token;
            var opened = false;
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var source = new Uri(_browser.CoreWebView2.Source);
                    if (IsRagnarokPlus(source) && source.AbsolutePath == "/login")
                    {
                        Show();
                        Activate();
                        throw new RagnarokPlusSignInRequiredException();
                    }
                    if (!IsRagnarokPlus(source) || source.AbsolutePath != itemPath)
                        throw new InvalidDataException("Unexpected Ragnarok+ item page.");

                    var json = await _browser.CoreWebView2.ExecuteScriptAsync(MarketScript(itemPath, opened)).WaitAsync(token);
                    var state = JsonSerializer.Deserialize<MarketPageState>(json);
                    if (state?.Dialog is { Length: > 0 } dialog)
                        return RagnarokPlusData.ParseLiveMarket(dialog, [item]).Snapshot.Listings;
                    if (state?.NoOffers == true) return [];
                    opened |= state?.Opened == true;
                    await Task.Delay(250, token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Ragnarok+ Live Market did not finish loading.");
            }
        }
        finally { _operation.Release(); }
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
            if (dialog) return { Dialog: dialog.innerText };
            if ({{(opened ? "true" : "false")}}) return null;
            const button = [...document.querySelectorAll('button')]
                .find(node => node.getClientRects().length && /^View\s+[\d,]+\s+shops?$/i.test(node.innerText.trim()));
            if (button) {
                button.click();
                return { Opened: true };
            }
            const lines = document.body.innerText.split('\n').map(value => value.trim());
            return { NoOffers: lines.includes('Market') &&
                lines.some(value => /^(No (open shops|active listings|active offers|market data)|0 open shops)\.?$/i.test(value)) };
        })()
        """;

    private sealed record MarketPageState(string? Dialog, bool NoOffers, bool Opened);
}

internal sealed class RagnarokPlusSignInRequiredException : Exception { }
