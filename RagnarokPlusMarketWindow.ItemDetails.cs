using System.Globalization;
using System.Text.Json;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

internal sealed partial class RagnarokPlusMarketWindow
{
    internal async Task EnrichItemsAsync(GameDatabase catalog, Action<GameDatabase> checkpoint, CancellationToken token)
    {
        await _operation.WaitAsync(token);
        var pending = 0;
        try
        {
            ShowBackground();
            await InitializeAsync();
            foreach (var item in catalog.Items.Where(item => !item.RagnarokPlusDetailsCaptured))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await ReadItemDetailsAsync(item, token);
                    if (++pending == 50)
                    {
                        checkpoint(catalog);
                        pending = 0;
                    }
                }
                catch (TimeoutException ex)
                {
                    DebugLog.Write($"RAGNAROK+ ITEM DETAILS #{item.Id}: {ex.Message}");
                }
                await Task.Delay(1000, token);
            }
        }
        finally
        {
            if (IsVisible) Hide();
            _operation.Release();
            if (pending > 0) checkpoint(catalog);
        }
    }

    private async Task ReadItemDetailsAsync(DatabaseItem item, CancellationToken token)
    {
        var path = $"/item-db/{item.Id}";
        await NavigateAsync(new Uri(Home, path), token);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            EnsurePage(path, interactive: false);
            var json = await _browser.CoreWebView2.ExecuteScriptAsync(ItemDetailsScript).WaitAsync(token);
            if (JsonSerializer.Deserialize<CatalogItemDetails>(json) is { } detail &&
                TryParseNpcSell(detail.Sell, out var sell))
            {
                byte[]? image = null;
                foreach (var url in detail.Images.Distinct())
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                        uri.Host != "cdn.ragnarok.plus" || !uri.IsDefaultPort) continue;
                    try
                    {
                        var source = await MapClient.GetByteArrayAsync(uri, token);
                        image = await EncodeCatalogImageAsync(source, token);
                        if (image is not null) break;
                    }
                    catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
                }
                item.Description = detail.Description;
                item.NpcSellPrice = sell;
                item.LocalImagePng = image;
                item.DroppedBy = detail.Drops.Concat(item.DroppedBy)
                    .DistinctBy(source => (source.MonsterId, source.IsMvp)).ToList();
                item.RagnarokPlusDetailsCaptured = true;
                return;
            }
            await Task.Delay(250, token);
        }
        throw new TimeoutException("NPC sell value was unavailable.");
    }

    private static bool TryParseNpcSell(string raw, out long sell)
    {
        var value = raw.Trim().TrimEnd('z', 'Z').Trim();
        sell = 0;
        if (value is "—" or "–" or "-" || value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            return true;
        value = value.Replace(",", "").Replace(" ", "").Replace("\u00a0", "").Replace("\u202f", "");
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out sell);
    }

    private async Task<byte[]?> EncodeCatalogImageAsync(byte[] source, CancellationToken token)
    {
        var expression = $$"""
            (async () => {
                const bytes = Uint8Array.from(atob({{JsonSerializer.Serialize(Convert.ToBase64String(source))}}),
                    value => value.charCodeAt(0));
                const image = await createImageBitmap(new Blob([bytes]));
                const scale = Math.min(1, 96 / Math.max(image.width, image.height));
                const canvas = document.createElement('canvas');
                canvas.width = Math.max(1, Math.round(image.width * scale));
                canvas.height = Math.max(1, Math.round(image.height * scale));
                const context = canvas.getContext('2d');
                context.imageSmoothingEnabled = false;
                context.drawImage(image, 0, 0, canvas.width, canvas.height);
                image.close();
                return canvas.toDataURL('image/png').split(',')[1];
            })()
            """;
        var parameters = JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true });
        var result = await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters).WaitAsync(token);
        using var json = JsonDocument.Parse(result);
        if (json.RootElement.TryGetProperty("result", out var value) && value.TryGetProperty("value", out var png) &&
            png.GetString() is { Length: > 0 } image)
            return Convert.FromBase64String(image);
        return null;
    }

    private const string ItemDetailsScript = """
        (() => {
            const sellLabel = [...document.querySelectorAll('main .id__row-label, main .id__qs-label')]
                .find(label => label.innerText.trim().toLowerCase() === 'sell');
            if (!sellLabel?.nextElementSibling) return null;
            const heading = [...document.querySelectorAll('main .db-section__head')]
                .find(node => node.innerText.trim().toLowerCase() === 'description');
            const description = heading?.nextElementSibling?.innerText.trim() ?? '';
            const images = ['.id__art', '.id__icon'].map(selector => document.querySelector('main ' + selector)?.src).filter(Boolean);
            const dropHeading = [...document.querySelectorAll('main .db-section__head')]
                .find(node => /^DROPPED BY MONSTERS\b/i.test(node.innerText.trim()));
            const showAll = [...(dropHeading?.closest('.db-section')?.querySelectorAll('button') ?? [])]
                .find(button => /^Show all \d+/i.test(button.innerText.trim()));
            if (showAll) {
                showAll.click();
                return null;
            }
            const drops = [...document.querySelectorAll('main a.db-rate-list__row[href^="/mob-db/"]')].flatMap(row => {
                const match = /^\/mob-db\/(\d+)$/.exec(new URL(row.href).pathname);
                const [name, ...fields] = row.innerText.trim().split('\n').map(field => field.trim());
                const rate = fields.find(field => /^[\d.,]+%$/.test(field));
                return match && name && rate ? [{ MonsterId: Number(match[1]), Name: name,
                    Rate: rate, IsMvp: fields.includes('MVP') }] : [];
            });
            return { Sell: sellLabel.nextElementSibling.innerText, Description: description, Images: images, Drops: drops };
        })()
        """;

    private sealed record CatalogItemDetails(string Sell, string Description, string[] Images, List<DatabaseItemSource> Drops);
}
