using System.Text.Json;
using RoAura.Services;

namespace RoAura;

internal sealed partial class RagnarokPlusMarketWindow
{
    internal async Task<StorageAnalysis.Entry[]> ReadStorageAsync(CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        await _operation.WaitAsync(cancellationToken);
        var wasVisible = IsVisible;
        var needsLogin = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var token = timeout.Token;
            if (!IsVisible) ShowBackground();
            await InitializeAsync();
            await NavigateAsync(new Uri(Home, "storage"), token);
            EnsurePage("/storage");
            const string start = """
                (() => {
                    if (location.origin !== 'https://ragnarok.plus' || location.pathname.replace(/\/$/, '') !== '/storage') return false;
                    window.__roAuraStorage = null;
                    window.__roAuraStorageProgress = null;
                    fetch('/api/account/storage', {credentials: 'same-origin', cache: 'no-store'}).then(async response => {
                        if (!response.ok) { window.__roAuraStorage = {Status: response.status}; return; }
                        window.__roAuraStorageProgress = {Done: 0, Total: 0};
                        const data = await response.json();
                        const keys = ['sc', 'sc2', 'sc3', 'hc', 'hc2', 'hc3'];
                        const ids = [...new Set(keys.flatMap(key => Array.isArray(data[key])
                            ? data[key].map(row => Number(row.nameid)).filter(id => Number.isSafeInteger(id) && id > 0) : []))];
                        const meta = {};
                        for (let index = 0; index < ids.length; index += 150) {
                            const chunk = ids.slice(index, index + 150).join(',');
                            try {
                                const response = await fetch('/api/items/meta?ids=' + chunk, {credentials: 'same-origin'});
                                if (response.ok) Object.assign(meta, await response.json());
                                window.__roAuraStorageProgress = {Done: Math.min(index + 150, ids.length), Total: ids.length};
                            } catch { /* Local catalog fills absent metadata. */ }
                        }
                        window.__roAuraStorage = {Status: response.status, Data: data, Meta: meta};
                    }).catch(() => { window.__roAuraStorage = {Status: 0}; });
                    return true;
                })()
                """;
            var core = _browser.CoreWebView2;
            if (await core.ExecuteScriptAsync(start).WaitAsync(token) != "true")
                throw new InvalidDataException("Could not start the Ragnarok+ storage request.");
            string? previousProgress = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                EnsurePage("/storage");
                var json = await core.ExecuteScriptAsync("window.__roAuraStorage ?? null").WaitAsync(token);
                if (json != "null")
                {
                    await core.ExecuteScriptAsync("delete window.__roAuraStorage").WaitAsync(token);
                    using var result = JsonDocument.Parse(json);
                    var status = result.RootElement.GetProperty("Status").GetInt32();
                    if (status is 401 or 403)
                    {
                        ShowInteractive();
                        core.Navigate(new Uri(Home, "login").AbsoluteUri);
                        throw new RagnarokPlusSignInRequiredException();
                    }
                    if (status != 200) throw new InvalidDataException($"Ragnarok+ storage request: HTTP {status}.");
                    return StorageAnalysis.Parse(result.RootElement.GetProperty("Data"), result.RootElement.GetProperty("Meta"),
                        (mode, slot, type) => progress?.Report($"{mode} · {slot} · {type}"));
                }
                var current = await core.ExecuteScriptAsync("window.__roAuraStorageProgress ?? null").WaitAsync(token);
                if (current != "null" && current != previousProgress)
                {
                    previousProgress = current;
                    using var state = JsonDocument.Parse(current);
                    progress?.Report($"Ragnarok+ · {state.RootElement.GetProperty("Done").GetInt32()}/{state.RootElement.GetProperty("Total").GetInt32()}");
                }
                await Task.Delay(200, token);
            }
        }
        catch (RagnarokPlusSignInRequiredException)
        {
            needsLogin = true;
            throw;
        }
        finally
        {
            if (!wasVisible && !needsLogin && IsVisible) Hide();
            _operation.Release();
        }
    }
}
