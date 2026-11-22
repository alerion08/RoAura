using System.Text.Json;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

internal sealed partial class RagnarokPlusMarketWindow
{
    internal async Task EnrichWorldAsync(GameDatabase catalog, Action<GameDatabase> checkpoint, CancellationToken token)
    {
        await _operation.WaitAsync(token);
        var pending = 0;
        try
        {
            ShowBackground();
            await InitializeAsync();
            var maps = catalog.Maps.ToDictionary(map => map.Code, StringComparer.OrdinalIgnoreCase);
            foreach (var monster in catalog.Monsters.Where(monster => !monster.RagnarokPlusDetailsCaptured))
            {
                var details = await ReadWorldPageAsync<MonsterDetails>($"/mob-db/{monster.Id}", MonsterDetailsScript, token);
                if (details is not null)
                {
                    monster.Drops = details.Drops;
                    monster.Spawns = details.Spawns;
                    monster.RagnarokPlusDetailsCaptured = true;
                    foreach (var spawn in monster.Spawns)
                    {
                        if (maps.ContainsKey(spawn.Code)) continue;
                        var map = new DatabaseMap { Code = spawn.Code, Name = spawn.Name };
                        maps.Add(map.Code, map);
                        catalog.Maps.Add(map);
                    }
                    if (++pending == 10) { checkpoint(catalog); pending = 0; }
                }
                await Task.Delay(500, token);
            }
            foreach (var map in catalog.Maps.Where(map => !map.RagnarokPlusDetailsCaptured))
            {
                var details = await ReadWorldPageAsync<MapDetails>($"/map-db/{Uri.EscapeDataString(map.Code)}", MapDetailsScript, token);
                if (details is not null)
                {
                    map.Name = details.Name;
                    map.Monsters = details.Monsters;
                    map.RagnarokPlusDetailsCaptured = true;
                    if (++pending == 10) { checkpoint(catalog); pending = 0; }
                }
                await Task.Delay(500, token);
            }
        }
        finally
        {
            if (IsVisible) Hide();
            _operation.Release();
            if (pending > 0) checkpoint(catalog);
        }
    }

    private async Task<T?> ReadWorldPageAsync<T>(string path, string script, CancellationToken token) where T : class
    {
        try
        {
            await NavigateAsync(new Uri(Home, path), token);
            for (var attempt = 0; attempt < 20; attempt++)
            {
                EnsurePage(path, interactive: false);
                var result = await _browser.CoreWebView2.ExecuteScriptAsync(script).WaitAsync(token);
                if (JsonSerializer.Deserialize<T>(result) is { } details) return details;
                await Task.Delay(250, token);
            }
        }
        catch (TimeoutException) { }
        DebugLog.Write($"RAGNAROK+ WORLD DETAILS unavailable: {path}");
        return null;
    }

    private const string MonsterDetailsScript = """
        (() => {
            const headings = [...document.querySelectorAll('main .db-section__head')];
            if (!headings.some(node => /^COMBAT STATS\b/i.test(node.innerText.trim()))) return null;
            const drops = [...document.querySelectorAll('main a.md__drop[href^="/item-db/"]')].flatMap(row => {
                const itemId = Number(new URL(row.href).pathname.split('/').pop());
                const [name, rate] = row.innerText.trim().split('\n').map(line => line.trim());
                return itemId > 0 && name && rate ? [{ ItemId: itemId, Name: name, Rate: rate,
                    IsMvp: row.classList.contains('md__drop--mvp') }] : [];
            });
            const spawns = [...document.querySelectorAll('main a.md__spawn-card[href^="/map-db/"]')].map(row => ({
                Code: decodeURIComponent(new URL(row.href).pathname.split('/').pop()),
                Name: row.querySelector('.md__spawn-name')?.innerText.trim() ?? '',
                Count: row.querySelector('.md__spawn-count')?.innerText.trim() ?? ''
            }));
            return { Drops: drops, Spawns: spawns };
        })()
        """;

    private const string MapDetailsScript = """
        (() => {
            const name = document.querySelector('main h1.page__title')?.innerText.trim();
            if (!name) return null;
            const monsters = [...document.querySelectorAll('main a.mapd__spawn-row[href^="/mob-db/"]')].flatMap(row => {
                const id = Number(new URL(row.href).pathname.split('/').pop());
                const [name, count, respawn, variance] = row.innerText.trim().split('\n')
                    .map(line => line.trim()).filter(line => line && line !== 'MVP' && line !== 'BOSS');
                return id > 0 && name && count ? [{ MonsterId: id, Name: name, Count: count,
                    Respawn: [respawn, variance].filter(value => value && value !== '-').join(' + ') }] : [];
            });
            return { Name: name, Monsters: monsters };
        })()
        """;

    private sealed record MonsterDetails(List<DatabaseDrop> Drops, List<DatabaseSpawn> Spawns);
    private sealed record MapDetails(string Name, List<DatabaseMapMonster> Monsters);
}
