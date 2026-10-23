using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RoAura.Models;

namespace RoAura.Services;

public sealed partial class SpringRoMarketService
{
    public event Action? DataUpdated;
    private readonly HttpClient _client;
    private readonly object _cacheSync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private readonly string _cachePath;
    private readonly string _snapshotPath;
    private readonly HashSet<string> _refreshing = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _snapshotRefreshLock = new(1, 1);
    private List<MarketListing> _snapshot = [];
    public bool HasSnapshot { get { lock (_cacheSync) return _snapshot.Count > 0; } }
    public bool IsRefreshingSnapshot => _snapshotRefreshLock.CurrentCount == 0;

    public SpringRoMarketService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 RoAura/2.12.1");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoAura");
        _cachePath = Path.Combine(directory, "springro-market-cache.json");
        _snapshotPath = Path.Combine(directory, "springro-market-snapshot.json");
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
            hasSnapshot = _snapshot.Count > 0;
            snapshotMatches = _snapshot.Where(x => x.ShopType.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                    x.ItemName.Contains(itemQuery, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(x => ParsePrice(x.Price)).Take(100).ToList();
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
            await RefreshInBackgroundAsync(item.Query, item.Type, item.Key, cancellationToken);
        }
    }

    public async Task RefreshSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!await _snapshotRefreshLock.WaitAsync(0, cancellationToken)) return;
        try
        {
            var all = new List<MarketListing>();
            foreach (var type in new[] { "SELL", "BUY" })
            {
                var firstUrl = $"https://springro.com/market?search=&page=1&sort=priceAsc&minRefine=0&shopType={type}";
                var firstHtml = await DownloadPageAsync(firstUrl, cancellationToken);
                var first = Parse(firstHtml, type);
                all.AddRange(first);
                var total = ParseTotal(firstHtml);
                var pages = Math.Max(1, (int)Math.Ceiling(total / 50d));
                if (pages <= 1) continue;

                // Market pages are independent. A modest parallel fan-out keeps the
                // initial snapshot practical without hammering SpringRO.
                using var gate = new SemaphoreSlim(8, 8);
                var pageTasks = Enumerable.Range(2, pages - 1).Select(async page =>
                {
                    await gate.WaitAsync(cancellationToken);
                    try
                    {
                        var url = $"https://springro.com/market?search=&page={page}&sort=priceAsc&minRefine=0&shopType={type}";
                        return Parse(await DownloadPageAsync(url, cancellationToken), type);
                    }
                    finally { gate.Release(); }
                });
                foreach (var page in await Task.WhenAll(pageTasks)) all.AddRange(page);
            }

            if (all.Count == 0) return;
            lock (_cacheSync) _snapshot = all;
            SaveSnapshot();
            DebugLog.Write($"MARKET SNAPSHOT REFRESHED: {all.Count} listings");
            DataUpdated?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DebugLog.Write($"MARKET SNAPSHOT REFRESH ERROR: {ex}"); }
        finally { _snapshotRefreshLock.Release(); }
    }

    private async Task<IReadOnlyList<MarketListing>> RefreshAndCacheAsync(string itemQuery, string type,
        string cacheKey, CancellationToken cancellationToken)
    {

        // The SpringRO page is server-rendered and can take many seconds. Loading one
        // filtered page is enough for the compact overlay and avoids the old 2-page wait.
        var url = "https://springro.com/market?search=" + Uri.EscapeDataString(itemQuery) +
                  $"&page=1&sort=priceAsc&minRefine=0&shopType={type}";
        var html = await DownloadPageAsync(url, cancellationToken);
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
            result = Parse(await DownloadPageAsync(secondUrl, cancellationToken), type)
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
        SavePersistentCache();
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
            await RefreshAndCacheAsync(query, type, cacheKey, cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DebugLog.Write($"MARKET BACKGROUND REFRESH ERROR: {ex.Message}"); }
        finally
        {
            lock (_cacheSync) _refreshing.Remove(cacheKey);
        }
    }

    private void LoadPersistentCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(
                File.ReadAllText(_cachePath));
            if (saved is null) return;
            lock (_cacheSync)
                foreach (var item in saved.Where(x => x.Value.Listings.Count > 0))
                    _cache[item.Key] = item.Value;
        }
        catch (Exception ex) { DebugLog.Write($"MARKET CACHE LOAD ERROR: {ex.Message}"); }
    }

    private void SavePersistentCache()
    {
        try
        {
            Dictionary<string, CacheEntry> snapshot;
            lock (_cacheSync) snapshot = new Dictionary<string, CacheEntry>(_cache, StringComparer.OrdinalIgnoreCase);
            var directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception ex) { DebugLog.Write($"MARKET CACHE SAVE ERROR: {ex.Message}"); }
    }

    private void LoadSnapshot()
    {
        try
        {
            if (!File.Exists(_snapshotPath)) return;
            var saved = JsonSerializer.Deserialize<List<MarketListing>>(File.ReadAllText(_snapshotPath));
            if (saved is not null) lock (_cacheSync) _snapshot = saved;
        }
        catch (Exception ex) { DebugLog.Write($"MARKET SNAPSHOT LOAD ERROR: {ex.Message}"); }
    }

    private void SaveSnapshot()
    {
        try
        {
            List<MarketListing> snapshot;
            lock (_cacheSync) snapshot = _snapshot.ToList();
            var directory = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_snapshotPath, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception ex) { DebugLog.Write($"MARKET SNAPSHOT SAVE ERROR: {ex.Message}"); }
    }

    private async Task<string> DownloadPageAsync(string url, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (attempt < 2 && !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        throw lastError ?? new HttpRequestException("SpringRO Market did not return a response.");
    }

    private static List<MarketListing> Parse(string html, string shopType)
    {
        var result = new List<MarketListing>();
        var dimensions = MapDimensionsRegex().Matches(html).Select(x => new
        {
            Map = x.Groups[1].Value,
            X = int.Parse(x.Groups[2].Value), Y = int.Parse(x.Groups[3].Value),
            Width = int.Parse(x.Groups[4].Value), Height = int.Parse(x.Groups[5].Value)
        }).GroupBy(x => $"{x.Map}|{x.X}|{x.Y}", StringComparer.OrdinalIgnoreCase)
          .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        foreach (Match row in RowRegex().Matches(html))
        {
            var cells = CellRegex().Matches(row.Groups[1].Value).Select(x => x.Groups[1].Value).ToList();
            if (cells.Count < 9) continue;
            var idMatch = ItemIdRegex().Match(cells[0]);
            var location = LocationRegex().Match(cells[8]);
            if (!idMatch.Success || !location.Success) continue;
            var map = location.Groups[1].Value;
            var x = int.Parse(location.Groups[2].Value);
            var y = int.Parse(location.Groups[3].Value);
            dimensions.TryGetValue($"{map}|{x}|{y}", out var mapSize);
            result.Add(new MarketListing
            {
                ItemId = int.Parse(idMatch.Groups[1].Value),
                ItemName = Plain(cells[1]), Refine = Plain(cells[2]), Cards = Plain(cells[3]),
                Price = Plain(cells[4]), Quantity = Plain(cells[5]), Stall = Plain(cells[6]),
                Owner = Plain(cells[7]), Map = map, X = x, Y = y,
                MapWidth = mapSize?.Width ?? 0, MapHeight = mapSize?.Height ?? 0,
                ShopType = shopType
            });
        }
        return result;
    }

    private static int ParseTotal(string html)
    {
        var match = TotalRegex().Match(html);
        return match.Success && int.TryParse(match.Groups[1].Value, out var total) ? total : 0;
    }

    private static long ParsePrice(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var price) ? price : long.MaxValue;
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
    [GeneratedRegex("data-item-id=\"(\\d+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ItemIdRegex();
    [GeneratedRegex("location:\\s*([A-Za-z0-9_]+)\\s*\\((\\d+),(\\d+)\\)", RegexOptions.IgnoreCase)]
    private static partial Regex LocationRegex();
    [GeneratedRegex("map:\"([^\"]+)\",x:(\\d+),y:(\\d+),mapDimensions:.*?\\{width:(\\d+),height:(\\d+)\\}", RegexOptions.IgnoreCase)]
    private static partial Regex MapDimensionsRegex();
    [GeneratedRegex("total:(\\d+),stallCount", RegexOptions.IgnoreCase)]
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
