using System.Globalization;
using System.Text.Json;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

internal sealed partial class RagnarokPlusMarketWindow
{
    internal async Task<GameDatabase> ReadCatalogAsync(CancellationToken token)
    {
        await _operation.WaitAsync(token);
        try
        {
            ShowBackground();
            await InitializeAsync();
            var existing = RagnarokPlusData.LoadCatalog();
            var items = await ReadCatalogSectionAsync("/item-db", "items", token);
            var monsters = await ReadCatalogSectionAsync("/mob-db", "monsters", token);
            var existingItems = existing.Items.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
            var existingMonsters = existing.Monsters.DistinctBy(monster => monster.Id).ToDictionary(monster => monster.Id);
            return new GameDatabase
            {
                Source = GameServerProfile.RagnarokPlus,
                Items = items.Select(row => ReadItem(row, existingItems)).ToList(),
                Monsters = monsters.Select(row => ReadMonster(row, existingMonsters)).ToList(),
                Maps = existing.Maps,
                CatalogSynchronizedUtc = DateTimeOffset.UtcNow
            };
        }
        finally
        {
            if (IsVisible) Hide();
            _operation.Release();
        }
    }

    private async Task<List<CatalogRow>> ReadCatalogSectionAsync(string path, string label, CancellationToken token)
    {
        await NavigateAsync(new Uri(Home, path), token);
        var rows = new List<CatalogRow>();
        var seen = new HashSet<int>();
        int? expectedTotal = null;
        var pageNumber = 1;
        while (true)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            CatalogPage page;
            try
            {
                while (true)
                {
                    EnsurePage(path, interactive: false);
                    var json = await _browser.CoreWebView2.ExecuteScriptAsync(CatalogScript(path, label))
                        .WaitAsync(deadline.Token);
                    var candidate = JsonSerializer.Deserialize<CatalogPage>(json);
                    if (candidate is { Total: > 0, Rows.Length: > 0 } && candidate.Page == pageNumber &&
                        candidate.Rows.All(row => row.Id > 0 && !seen.Contains(row.Id)))
                    {
                        page = candidate;
                        break;
                    }
                    await Task.Delay(250, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException($"Ragnarok+ catalog {label} page {pageNumber} timed out.");
            }

            expectedTotal ??= page.Total;
            if (expectedTotal != page.Total || page.Rows.Any(row => !seen.Add(row.Id)))
                throw new InvalidDataException($"The Ragnarok+ {label} catalog changed during synchronization.");
            rows.AddRange(page.Rows);
            if (!page.HasNext && rows.Count == expectedTotal) return rows;
            if (!page.HasNext || rows.Count >= expectedTotal)
                throw new InvalidDataException($"Ragnarok+ returned {rows.Count} of {expectedTotal} {label}.");

            await Task.Delay(500, token);
            var clicked = await _browser.CoreWebView2.ExecuteScriptAsync("""
                (() => {
                    const next = document.querySelector('button[aria-label="Next Page"]');
                    if (!next || next.disabled) return false;
                    next.click();
                    return true;
                })()
                """).WaitAsync(token);
            if (clicked != "true") throw new InvalidDataException("Ragnarok+ catalog pagination is unavailable.");
            pageNumber++;
        }
    }

    private static string CatalogScript(string path, string label) => $$"""
        (() => {
            const path = {{JsonSerializer.Serialize(path)}};
            const label = {{JsonSerializer.Serialize(label)}};
            if (location.origin !== 'https://ragnarok.plus' || location.pathname !== path) return null;
            const count = [...document.querySelectorAll('strong')].find(node =>
                node.getClientRects().length && node.parentElement.textContent.trim().toLowerCase().replace(/\s+/g, ' ').endsWith(' ' + label));
            const total = Number((count?.textContent ?? '').replace(/[^0-9]/g, ''));
            const selected = document.querySelector('button[data-type="page"][aria-current]');
            const next = document.querySelector('button[aria-label="Next Page"]');
            const rows = [...document.querySelectorAll('a[href^="' + path + '/"]')].flatMap(node => {
                if (!node.getClientRects().length) return [];
                const lines = node.innerText.split(/\n/).map(line => line.trim()).filter(Boolean);
                const id = Number(new URL(node.href).pathname.split('/').pop());
                if (!id || !lines.length || lines.at(-1) !== '#' + id) return [];
                return [{ Id: id, Lines: lines.slice(0, -1) }];
            });
            return { Total: total, Page: selected ? Number(selected.value) : (next ? 0 : 1),
                HasNext: !!next && !next.disabled, Rows: rows };
        })()
        """;

    private static DatabaseItem ReadItem(CatalogRow row, Dictionary<int, DatabaseItem> existing)
    {
        if (row.Lines is not [var name, var type])
            throw new InvalidDataException($"Unexpected Ragnarok+ item row #{row.Id}.");
        var item = existing.GetValueOrDefault(row.Id) ?? new DatabaseItem { Id = row.Id };
        item.Name = name;
        item.Type = TitleCase(type);
        return item;
    }

    private static DatabaseMonster ReadMonster(CatalogRow row, Dictionary<int, DatabaseMonster> existing)
    {
        if (row.Lines is not [var name, .. var flags, var level, var race, var element, var size,
                "B", var baseExp, "J", var jobExp] || !level.StartsWith("LV ", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(level.AsSpan(3), out _) || flags.Any(flag => flag.ToUpperInvariant() is not ("MVP" or "BOSS")))
            throw new InvalidDataException($"Unexpected Ragnarok+ monster row #{row.Id}.");
        var monster = existing.GetValueOrDefault(row.Id) ?? new DatabaseMonster { Id = row.Id };
        monster.Name = name;
        monster.Level = level[3..];
        monster.Race = TitleCase(race);
        monster.Element = TitleCase(element);
        monster.Size = TitleCase(size);
        monster.BaseExp = ReadExperience(baseExp);
        monster.JobExp = ReadExperience(jobExp);
        monster.IsMvp = flags.Contains("MVP", StringComparer.OrdinalIgnoreCase);
        monster.IsBoss |= flags.Length > 0;
        return monster;
    }

    private static string TitleCase(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());

    private static string ReadExperience(string text) =>
        long.Parse(text, NumberStyles.AllowThousands, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private sealed record CatalogPage(int Total, int Page, bool HasNext, CatalogRow[] Rows);
    private sealed record CatalogRow(int Id, string[] Lines);
}
