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
    private readonly HttpClient _client;
    private readonly object _cacheSync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private readonly string _cachePath;
    private readonly HashSet<string> _refreshing = new(StringComparer.OrdinalIgnoreCase);

    public SpringRoMarketService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 RoAura/2.11.0");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoAura");
        _cachePath = Path.Combine(directory, "springro-market-cache.json");
        LoadPersistentCache();
    }

    public async Task<IReadOnlyList<MarketListing>> SearchAsync(string query, string shopType,
        CancellationToken cancellationToken = default)
    {
        var itemQuery = query.Trim();
        var type = shopType.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL";
        if (itemQuery.Length == 0) return Array.Empty<MarketListing>();

        var cacheKey = $"{type}|{itemQuery.ToUpperInvariant()}";
        CacheEntry? stale = null;
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(cacheKey, out var cached) &&
                DateTime.UtcNow - cached.CreatedUtc < CacheLifetime)
                return cached.Listings;
            if (cached is not null && cached.Listings.Count > 0) stale = cached;
        }

        // Never make the user wait for SpringRO when usable cached data exists.
        // Refresh it in the background and replace it only after a successful load.
        if (stale is not null)
        {
            _ = RefreshInBackgroundAsync(itemQuery, type, cacheKey);
            return stale.Listings;
        }

        return await RefreshAndCacheAsync(itemQuery, type, cacheKey, cancellationToken);
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
        foreach (Match row in RowRegex().Matches(html))
        {
            var cells = CellRegex().Matches(row.Groups[1].Value).Select(x => x.Groups[1].Value).ToList();
            if (cells.Count < 9) continue;
            var idMatch = ItemIdRegex().Match(cells[0]);
            var location = LocationRegex().Match(cells[8]);
            if (!idMatch.Success || !location.Success) continue;
            result.Add(new MarketListing
            {
                ItemId = int.Parse(idMatch.Groups[1].Value),
                ItemName = Plain(cells[1]), Refine = Plain(cells[2]), Cards = Plain(cells[3]),
                Price = Plain(cells[4]), Quantity = Plain(cells[5]), Stall = Plain(cells[6]),
                Owner = Plain(cells[7]), Map = location.Groups[1].Value,
                X = int.Parse(location.Groups[2].Value), Y = int.Parse(location.Groups[3].Value),
                ShopType = shopType
            });
        }
        return result;
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
