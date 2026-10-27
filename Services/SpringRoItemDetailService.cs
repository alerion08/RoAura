using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using RoAura.Models;

namespace RoAura.Services;

public sealed partial class SpringRoItemDetailService
{
    private readonly HttpClient _client;
    private readonly ConcurrentDictionary<int, CacheEntry> _cache = new();
    private static readonly Regex NpcSellValueRegex = new(
        Regex.Escape("NPC sell value") + "</dt><dd[^>]*>(.*?)</dd>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public SpringRoItemDetailService()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 RoAura/{version}");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
    }

    public async Task<SpringRoItemDetails> GetAsync(int itemId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(itemId, out var cached) &&
            DateTime.UtcNow - cached.CreatedUtc < TimeSpan.FromMinutes(5))
            return cached.Value;

        var html = await _client.GetStringAsync($"https://springro.com/database/items/{itemId}", cancellationToken);
        html = html.Replace("\0", "", StringComparison.Ordinal);
        var detail = new SpringRoItemDetails
        {
            ImageUrl = ImageRegex().Match(html).Groups[1].Value,
            NpcSellValue = Plain(NpcSellValueRegex.Match(html).Groups[1].Value),
            ServerCopies = Plain(CirculationRegex().Match(html).Groups[1].Value),
            Description = ParseDescription(html)
        };
        if (string.IsNullOrWhiteSpace(detail.ImageUrl))
            detail.ImageUrl = $"https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/items/{itemId}.png";
        _cache[itemId] = new CacheEntry(DateTime.UtcNow, detail);
        return detail;
    }

    private static string ParseDescription(string html)
    {
        var match = DescriptionRegex().Match(html);
        if (!match.Success) return "";
        var paragraphs = ParagraphRegex().Matches(match.Groups[1].Value)
            .Select(x => Plain(x.Groups[1].Value))
            .Where(x => !string.IsNullOrWhiteSpace(x) && x != "_")
            .ToList();
        return string.Join(Environment.NewLine, paragraphs);
    }

    private static string Plain(string html) =>
        WebUtility.HtmlDecode(TagRegex().Replace(html, " ")).Replace("  ", " ").Trim();

    [GeneratedRegex("<meta property=\"og:image\" content=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ImageRegex();
    [GeneratedRegex("aria-label=\"Description and effects\".*?<div[^>]*>(.*?)</div></section>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DescriptionRegex();
    [GeneratedRegex("<p[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ParagraphRegex();
    [GeneratedRegex("<span[^>]*>([0-9,]+)</span><span[^>]*>copies(?:<!--.*?-->)? on the server", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CirculationRegex();
    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    private sealed record CacheEntry(DateTime CreatedUtc, SpringRoItemDetails Value);
}
