using System.Text.Json;
using RoAura.Services;

namespace RoAura;

internal sealed partial class RagnarokPlusMarketWindow
{
    internal async Task<StorageAnalysis.Entry[]> ReadStorageAsync(CancellationToken cancellationToken)
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
                    fetch('/api/account/storage', {credentials: 'same-origin', cache: 'no-store'}).then(async response => {
                        if (!response.ok) { window.__roAuraStorage = {Status: response.status}; return; }
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
                    if (status == 401)
                    {
                        ShowInteractive();
                        core.Navigate(new Uri(Home, "login").AbsoluteUri);
                        throw new RagnarokPlusSignInRequiredException();
                    }
                    if (status != 200) throw new InvalidDataException($"Ragnarok+ storage request: HTTP {status}.");
                    return StorageAnalysis.Parse(result.RootElement.GetProperty("Data"), result.RootElement.GetProperty("Meta"));
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
