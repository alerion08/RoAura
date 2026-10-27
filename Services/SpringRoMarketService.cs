using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RoAura.Models;

namespace RoAura.Services;

public sealed partial class SpringRoMarketService
{
    public event Action? DataUpdated;
    // Unlike DataUpdated this excludes cached/per-query results and failed or partial crawls.
    public event Action<IReadOnlyList<MarketListing>>? SnapshotUpdated;
    private readonly HttpClient _client;
    private readonly object _cacheSync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultThrottleBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumThrottleBackoff = TimeSpan.FromMinutes(30);
    private const int SnapshotParallelism = 4;
    private readonly string _cachePath;
    private readonly string _snapshotPath;
    private readonly string _legacyCachePath;
    private readonly string _legacySnapshotPath;
    private readonly HashSet<string> _refreshing = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _snapshotRefreshLock = new(1, 1);
    private bool _hasSnapshot;
    private List<MarketListing> _snapshot = [];
    // Parallel to _snapshot: numeric price per row (long.MaxValue when unparsable).
    private long[] _snapshotPrices = [];
    private Dictionary<int, long> _minSellByItemId = new();
    private Dictionary<string, long> _minSellByItemName = new(StringComparer.CurrentCultureIgnoreCase);
    private DateTime _throttledUntilUtc = DateTime.MinValue;
    public bool HasSnapshot { get { lock (_cacheSync) return _hasSnapshot; } }
    public bool IsRefreshingSnapshot => _snapshotRefreshLock.CurrentCount == 0;

    public bool TryGetMinimumSellPrice(int itemId, string itemName, out long price)
    {
        lock (_cacheSync)
        {
            if (itemId > 0)
            {
                if (_minSellByItemId.TryGetValue(itemId, out price)) return true;
            }
            else if (!string.IsNullOrEmpty(itemName) && _minSellByItemName.TryGetValue(itemName, out price))
                return true;
        }
        price = 0;
        return false;
    }

    public SpringRoMarketService(HttpMessageHandler? messageHandler = null, string? cacheDirectory = null)
    {
        var handler = messageHandler ?? new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All
        };
        _client = new HttpClient(handler) { Timeout = PageTimeout };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 RoAura/{version}");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        var directory = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoAura");
        _cachePath = Path.Combine(directory, "springro-market-cache.json");
        _snapshotPath = Path.Combine(directory, "springro-market-snapshot.json");
        // Older builds kept the files under Roaming; read them once as a fallback so
        // the cache is not lost. New writes always go to LocalApplicationData.
        var legacyDirectory = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoAura");
        _legacyCachePath = Path.Combine(legacyDirectory, "springro-market-cache.json");
        _legacySnapshotPath = Path.Combine(legacyDirectory, "springro-market-snapshot.json");
        LoadPersistentCache();
        LoadSnapshot();
    }

    public Task<IReadOnlyList<MarketListing>> SearchAsync(string query, string shopType,
        CancellationToken cancellationToken = default)
    {
        var itemQuery = query.Trim();
        var type = shopType.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL";
        if (itemQuery.Length == 0) return Task.FromResult<IReadOnlyList<MarketListing>>(Array.Empty<MarketListing>());

        List<MarketListing> snapshotMatches;
        bool hasSnapshot;
        lock (_cacheSync)
        {
            hasSnapshot = _hasSnapshot;
            var prices = _snapshotPrices;
            snapshotMatches = _snapshot
                .Select((listing, index) => (Listing: listing, Price: index < prices.Length ? prices[index] : long.MaxValue))
                .Where(x => x.Listing.ShopType.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                            x.Listing.ItemName.Contains(itemQuery, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(x => x.Price).Take(100).Select(x => x.Listing).ToList();
        }
        if (hasSnapshot) return Task.FromResult<IReadOnlyList<MarketListing>>(snapshotMatches);

        var cacheKey = $"{type}|{itemQuery.ToUpperInvariant()}";
        CacheEntry? stale = null;
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(cacheKey, out var cached) &&
                DateTime.UtcNow - cached.CreatedUtc < CacheLifetime)
                return Task.FromResult<IReadOnlyList<MarketListing>>(cached.Listings);
            if (cached is not null && cached.Listings.Count > 0) stale = cached;
        }

        // Never make the user wait for SpringRO when usable cached data exists.
        // Refresh it in the background and replace it only after a successful load.
        if (stale is not null)
        {
            _ = RefreshInBackgroundAsync(itemQuery, type, cacheKey);
            return Task.FromResult<IReadOnlyList<MarketListing>>(stale.Listings);
        }

        // First use must never freeze the overlay while SpringRO renders a page.
        // Return immediately and publish the result through DataUpdated.
        _ = RefreshInBackgroundAsync(itemQuery, type, cacheKey, cancellationToken);
        return Task.FromResult<IReadOnlyList<MarketListing>>(Array.Empty<MarketListing>());
    }

    public async Task RefreshKnownQueriesAsync(CancellationToken cancellationToken = default)
    {
        // Once a full snapshot exists SearchAsync answers from it and the per-query
        // cache is never read, so refreshing it would only add load on SpringRO.
        if (HasSnapshot) return;

        List<(string Query, string Type, string Key)> known;
        lock (_cacheSync)
        {
            known = _cache.OrderByDescending(x => x.Value.CreatedUtc).Take(20)
                .Select(x =>
                {
                    var separator = x.Key.IndexOf('|');
                    return separator > 0
                        ? (x.Key[(separator + 1)..], x.Key[..separator], x.Key)
                        : ("", "", "");
                })
                .Where(x => x.Item1.Length > 0).ToList();
        }

        foreach (var item in known)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await RefreshInBackgroundAsync(item.Query, item.Type, item.Key, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RefreshSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!await _snapshotRefreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        // Cancelled when SpringRO throttles us, or when this cycle fails for any
        // reason, so page tasks still in flight stop promptly. Not disposed: the
        // straggling tasks may still touch its token after this method has returned.
        var cycle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            DateTime throttledUntil;
            lock (_cacheSync) throttledUntil = _throttledUntilUtc;
            if (now < throttledUntil)
            {
                DebugLog.Write($"MARKET SNAPSHOT REFRESH SKIPPED: SpringRO back-off for another {(throttledUntil - now).TotalSeconds:F0} s");
                return;
            }
            var all = new List<MarketListing>();
            foreach (var type in new[] { "SELL", "BUY" })
            {
                var firstUrl = $"https://springro.com/market?search=&page=1&sort=priceAsc&minRefine=0&shopType={type}";
                var firstHtml = await DownloadPageAsync(firstUrl, cycle.Token).ConfigureAwait(false);
                var first = Parse(firstHtml, type);
                all.AddRange(first);
                if (!TotalRegex().IsMatch(firstHtml))
                    throw new InvalidDataException("SpringRO market page has no total; preserving the last snapshot.");
                var total = ParseTotal(firstHtml);
                if (first.Count != Math.Min(50, total))
                    throw new InvalidDataException("SpringRO market rows could not be parsed; preserving the last snapshot.");
                var pages = Math.Max(1, (int)Math.Ceiling(total / 50d));
                if (pages <= 1) continue;

                // Market pages are independent. A modest parallel fan-out keeps the
                // initial snapshot practical without hammering SpringRO. (Not disposed
                // for the same reason as the cycle token source above.)
                var gate = new SemaphoreSlim(SnapshotParallelism, SnapshotParallelism);
                var pageTasks = Enumerable.Range(2, pages - 1).Select(async page =>
                {
                    await gate.WaitAsync(cycle.Token).ConfigureAwait(false);
                    try
                    {
                        var url = $"https://springro.com/market?search=&page={page}&sort=priceAsc&minRefine=0&shopType={type}";
                        var rows = Parse(await DownloadPageAsync(url, cycle.Token).ConfigureAwait(false), type);
                        if (rows.Count != Math.Min(50, total - (page - 1) * 50))
                            throw new InvalidDataException("Incomplete market page; preserving the last snapshot.");
                        return rows;
                    }
                    catch (HttpRequestException ex) when (IsThrottled(ex.StatusCode))
                    {
                        // Stop the remaining page fetches; this cycle is abandoned.
                        cycle.Cancel();
                        throw;
                    }
                    finally { gate.Release(); }
                });
                foreach (var page in await Task.WhenAll(pageTasks).ConfigureAwait(false)) all.AddRange(page);
            }

            lock (_cacheSync) SetSnapshot(all);
            await SaveSnapshotAsync().ConfigureAwait(false);
            DebugLog.Write($"MARKET SNAPSHOT REFRESHED: {all.Count} listings");
            SnapshotUpdated?.Invoke(all.AsReadOnly());
            DataUpdated?.Invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            DebugLog.Write("MARKET SNAPSHOT REFRESH ABORTED: SpringRO throttled the crawl (HTTP 429/5xx); will retry next cycle");
        }
        catch (HttpRequestException ex) when (IsThrottled(ex.StatusCode))
        {
            DebugLog.Write($"MARKET SNAPSHOT REFRESH ABORTED: SpringRO throttled the crawl ({(int?)ex.StatusCode}); will retry next cycle");
        }
        catch (Exception ex) { DebugLog.Write($"MARKET SNAPSHOT REFRESH ERROR: {ex}"); }
        finally
        {
            try { cycle.Cancel(); }
            catch (Exception ex) { DebugLog.Write($"MARKET SNAPSHOT CYCLE CANCEL ERROR: {ex}"); }
            _snapshotRefreshLock.Release();
        }
    }

    private async Task<IReadOnlyList<MarketListing>> RefreshAndCacheAsync(string itemQuery, string type,
        string cacheKey, CancellationToken cancellationToken)
    {

        // The SpringRO page is server-rendered and can take many seconds. Loading one
        // filtered page is enough for the compact overlay and avoids the old 2-page wait.
        var url = "https://springro.com/market?search=" + Uri.EscapeDataString(itemQuery) +
                  $"&page=1&sort=priceAsc&minRefine=0&shopType={type}";
        var html = await DownloadPageAsync(url, cancellationToken).ConfigureAwait(false);
        var pageRows = Parse(html, type);
        var result = pageRows
            .Where(x => itemQuery.Length == 0 ||
                        x.ItemName.Contains(itemQuery, StringComparison.CurrentCultureIgnoreCase))
            .Take(100)
            .ToList();

        // SpringRO also searches stall/owner names. If those unrelated matches fill
        // the first page, check the second page before reporting that the item is absent.
        if (result.Count == 0 && pageRows.Count >= 50)
        {
            var secondUrl = "https://springro.com/market?search=" + Uri.EscapeDataString(itemQuery) +
                            $"&page=2&sort=priceAsc&minRefine=0&shopType={type}";
            result = Parse(await DownloadPageAsync(secondUrl, cancellationToken).ConfigureAwait(false), type)
                .Where(x => x.ItemName.Contains(itemQuery, StringComparison.CurrentCultureIgnoreCase))
                .Take(100)
                .ToList();
        }

        lock (_cacheSync)
        {
            _cache[cacheKey] = new CacheEntry(DateTime.UtcNow, result);
            foreach (var excess in _cache.OrderByDescending(x => x.Value.CreatedUtc).Skip(50)
                         .Select(x => x.Key).ToList()) _cache.Remove(excess);
        }
        await SavePersistentCacheAsync().ConfigureAwait(false);
        DataUpdated?.Invoke();
        return result;
    }

    private async Task RefreshInBackgroundAsync(string query, string type, string cacheKey,
        CancellationToken cancellationToken = default)
    {
        lock (_cacheSync)
        {
            if (!_refreshing.Add(cacheKey)) return;
        }
        try
        {
            await RefreshAndCacheAsync(query, type, cacheKey, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DebugLog.Write($"MARKET BACKGROUND REFRESH ERROR: {ex}"); }
        finally
        {
            lock (_cacheSync) _refreshing.Remove(cacheKey);
        }
    }

    /// <summary>Replaces the snapshot and rebuilds the price indexes. Caller holds <see cref="_cacheSync"/>.</summary>
    private void SetSnapshot(List<MarketListing> listings)
    {
        var prices = new long[listings.Count];
        var byId = new Dictionary<int, long>();
        var byName = new Dictionary<string, long>(StringComparer.CurrentCultureIgnoreCase);
        for (var index = 0; index < listings.Count; index++)
        {
            var listing = listings[index];
            var price = ParsePrice(listing.Price);
            prices[index] = price;
            if (price == long.MaxValue || !listing.ShopType.Equals("SELL", StringComparison.OrdinalIgnoreCase)) continue;
            if (listing.ItemId > 0 && (!byId.TryGetValue(listing.ItemId, out var currentById) || price < currentById))
                byId[listing.ItemId] = price;
            if (listing.ItemName.Length > 0 &&
                (!byName.TryGetValue(listing.ItemName, out var currentByName) || price < currentByName))
                byName[listing.ItemName] = price;
        }
        _hasSnapshot = true;
        _snapshot = listings;
        _snapshotPrices = prices;
        _minSellByItemId = byId;
        _minSellByItemName = byName;
    }

    private void LoadPersistentCache()
    {
        try
        {
            var path = File.Exists(_cachePath) ? _cachePath : _legacyCachePath;
            if (!File.Exists(path)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(
                File.ReadAllText(path));
            if (saved is null) return;
            lock (_cacheSync)
                foreach (var item in saved.Where(x => x.Value.Listings.Count > 0))
                    _cache[item.Key] = item.Value;
        }
        catch (Exception ex) { DebugLog.Write($"MARKET CACHE LOAD ERROR: {ex}"); }
    }

    private Task SavePersistentCacheAsync()
    {
        Dictionary<string, CacheEntry> snapshot;
        lock (_cacheSync) snapshot = new Dictionary<string, CacheEntry>(_cache, StringComparer.OrdinalIgnoreCase);
        return Task.Run(() =>
        {
            try { WriteJsonAtomically(_cachePath, snapshot); }
            catch (Exception ex) { DebugLog.Write($"MARKET CACHE SAVE ERROR: {ex}"); }
        });
    }

    private void LoadSnapshot()
    {
        try
        {
            var path = File.Exists(_snapshotPath) ? _snapshotPath : _legacySnapshotPath;
            if (!File.Exists(path)) return;
            var saved = JsonSerializer.Deserialize<List<MarketListing>>(File.ReadAllText(path));
            if (saved is null) return;
            lock (_cacheSync) SetSnapshot(saved);
        }
        catch (Exception ex) { DebugLog.Write($"MARKET SNAPSHOT LOAD ERROR: {ex}"); }
    }

    private Task SaveSnapshotAsync()
    {
        List<MarketListing> snapshot;
        lock (_cacheSync) snapshot = _snapshot.ToList();
        return Task.Run(() =>
        {
            try { WriteJsonAtomically(_snapshotPath, snapshot); }
            catch (Exception ex) { DebugLog.Write($"MARKET SNAPSHOT SAVE ERROR: {ex}"); }
        });
    }

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { }
        }
    }

    private async Task<string> DownloadPageAsync(string url, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            // HttpClient.Timeout only bounds the whole request when the body is buffered
            // (ResponseContentRead). The linked token additionally bounds the attempt when
            // the caller passed CancellationToken.None.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PageTimeout);
            try
            {
                using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseContentRead, timeout.Token)
                    .ConfigureAwait(false);
                if (IsThrottled(response.StatusCode))
                {
                    // Do not retry a throttled request: the snapshot crawl aborts and
                    // waits out the back-off before the next cycle instead.
                    var backoff = RetryAfter(response.Headers) ?? DefaultThrottleBackoff;
                    lock (_cacheSync) _throttledUntilUtc = DateTime.UtcNow + backoff;
                    DebugLog.Write($"MARKET THROTTLED ({(int)response.StatusCode}) on {url}; backing off {backoff.TotalSeconds:F0} s");
                }
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex) when (attempt < 2)
            {
                // Our own per-attempt timeout fired: retry once like any other transient failure.
                lastError = new TimeoutException($"SpringRO Market did not respond within {PageTimeout.TotalSeconds:F0} s.", ex);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                throw new TimeoutException($"SpringRO Market did not respond within {PageTimeout.TotalSeconds:F0} s.", ex);
            }
            catch (HttpRequestException ex) when (IsThrottled(ex.StatusCode))
            {
                throw;
            }
            catch (Exception ex) when (attempt < 2)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
        throw lastError ?? new HttpRequestException("SpringRO Market did not return a response.");
    }

    private static bool IsThrottled(HttpStatusCode? status) =>
        status is HttpStatusCode.TooManyRequests || (status is not null && (int)status.Value >= 500);

    private static TimeSpan? RetryAfter(HttpResponseHeaders headers)
    {
        var retryAfter = headers.RetryAfter;
        TimeSpan? value = null;
        if (retryAfter?.Delta is not null) value = retryAfter.Delta;
        else if (retryAfter?.Date is not null) value = retryAfter.Date.Value - DateTimeOffset.UtcNow;
        if (value is null || value.Value <= TimeSpan.Zero) return null;
        return value.Value > MaximumThrottleBackoff ? MaximumThrottleBackoff : value;
    }

    private static List<MarketListing> Parse(string html, string shopType)
    {
        var result = new List<MarketListing>();
        var dimensions = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MapDimensionsRegex().Matches(html))
        {
            if (!TryInt(match.Groups[2].Value, out var x) || !TryInt(match.Groups[3].Value, out var y) ||
                !TryInt(match.Groups[4].Value, out var width) || !TryInt(match.Groups[5].Value, out var height))
                continue;
            dimensions.TryAdd($"{match.Groups[1].Value}|{x}|{y}", (width, height));
        }
        foreach (Match row in RowRegex().Matches(html))
        {
            var cells = CellRegex().Matches(row.Groups[1].Value).Select(x => x.Groups[1].Value).ToList();
            if (cells.Count < 9) continue;
            var idMatch = ItemIdRegex().Match(cells[0]);
            var location = LocationRegex().Match(cells[8]);
            if (!idMatch.Success || !location.Success) continue;
            if (!TryInt(idMatch.Groups[1].Value, out var itemId) ||
                !TryInt(location.Groups[2].Value, out var x) ||
                !TryInt(location.Groups[3].Value, out var y))
                continue;
            var map = location.Groups[1].Value;
            dimensions.TryGetValue($"{map}|{x}|{y}", out var mapSize);
            result.Add(new MarketListing
            {
                ItemId = itemId,
                ItemName = Plain(cells[1]), Refine = Plain(cells[2]), Cards = Plain(cells[3]),
                Price = Plain(cells[4]), Quantity = Plain(cells[5]), Stall = Plain(cells[6]),
                Owner = Plain(cells[7]), Map = map, X = x, Y = y,
                MapWidth = mapSize.Width, MapHeight = mapSize.Height,
                ShopType = shopType
            });
        }
        return result;
    }

    private static int ParseTotal(string html)
    {
        var match = TotalRegex().Match(html);
        return match.Success && TryInt(match.Groups[1].Value, out var total) ? total : 0;
    }

    private static bool TryInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    /// <summary>Digits-only price ("1,250,000 z" → 1250000) without allocating; long.MaxValue when absent or overflowing.</summary>
    public static long ParsePrice(string value)
    {
        long price = 0;
        var digits = 0;
        foreach (var character in value)
        {
            if (character < '0' || character > '9') continue;
            if (price > (long.MaxValue - 9) / 10) return long.MaxValue;
            price = price * 10 + (character - '0');
            digits++;
        }
        return digits > 0 ? price : long.MaxValue;
    }

    private static string Plain(string html)
    {
        html = CommentRegex().Replace(html, "");
        html = TagRegex().Replace(html, "");
        return WebUtility.HtmlDecode(html).Trim();
    }

    [GeneratedRegex("<tr>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RowRegex();
    [GeneratedRegex("<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellRegex();
    [GeneratedRegex("data-item-id=\"([0-9]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ItemIdRegex();
    [GeneratedRegex("location:\\s*([A-Za-z0-9_]+)\\s*\\(([0-9]+),([0-9]+)\\)", RegexOptions.IgnoreCase)]
    private static partial Regex LocationRegex();
    [GeneratedRegex("map:\"([^\"]+)\",x:([0-9]+),y:([0-9]+),mapDimensions:.*?\\{width:([0-9]+),height:([0-9]+)\\}", RegexOptions.IgnoreCase)]
    private static partial Regex MapDimensionsRegex();
    [GeneratedRegex("total:([0-9]+),stallCount", RegexOptions.IgnoreCase)]
    private static partial Regex TotalRegex();
    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();
    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    private sealed class CacheEntry
    {
        public DateTime CreatedUtc { get; set; }
        public List<MarketListing> Listings { get; set; } = [];

        public CacheEntry() { }
        public CacheEntry(DateTime createdUtc, IReadOnlyList<MarketListing> listings)
        {
            CreatedUtc = createdUtc;
            Listings = listings.ToList();
        }
    }
}
