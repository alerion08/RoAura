using System.IO.Compression;
using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

internal static class RagnarokPlusData
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly object MarketLock = new();
    private static DateTime _marketWriteUtc;
    private static MarketSnapshot? _market;

    internal static event Action? MarketImported;

    private static string MarketPath => Path.Combine(AppEnvironment.LocalDirectory, "market.ragnarokplus.json");

    internal static int ImportMarket(string sourcePath)
    {
        var text = File.ReadAllText(sourcePath);
        if (Path.GetExtension(sourcePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var import = ParseLiveMarket(text, LoadCatalog().Items);
            return SaveMarket(import.Snapshot, import.ItemId);
        }
        return SaveMarket(JsonSerializer.Deserialize<MarketSnapshot>(text, JsonOptions));
    }

    internal static void RememberLiveOffers(int itemId, IReadOnlyList<MarketListing> listings)
    {
        SaveMarket(new MarketSnapshot
        {
            CapturedUtc = DateTimeOffset.UtcNow,
            Listings = listings.ToList()
        }, itemId);
    }

    private static int SaveMarket(MarketSnapshot? snapshot, int? itemId = null)
    {
        if (snapshot is null || snapshot.CapturedUtc == default || snapshot.Listings is null ||
            snapshot.Listings.Any(row => row is null || row.ItemId <= 0 ||
                string.IsNullOrWhiteSpace(row.ItemName) || row.ShopType is not ("BUY" or "SELL") ||
                row.GameMode is not ("SC" or "HC") ||
                SpringRoMarketService.ParsePrice(row.Price ?? "") is <= 0 or long.MaxValue))
            throw new InvalidDataException("Invalid Ragnarok+ market snapshot.");

        var importedCount = snapshot.Listings.Count;
        var path = MarketPath;
        lock (MarketLock)
        {
            if (LoadMarket() is { } previous && previous.CapturedUtc > snapshot.CapturedUtc)
                throw new InvalidDataException("The imported market snapshot is older than the current snapshot.");

            foreach (var row in snapshot.Listings)
                row.ObservedUtc ??= snapshot.CapturedUtc;
            if (itemId.HasValue && LoadMarket() is { } existing)
            {
                var retained = existing.Listings.Where(row => row is not null && row.ItemId != itemId).ToArray();
                foreach (var row in retained)
                    row.ObservedUtc ??= existing.CapturedUtc;
                snapshot.Listings.InsertRange(0, retained);
            }

            Directory.CreateDirectory(AppEnvironment.LocalDirectory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
                File.Move(temporary, path, overwrite: true);
                _market = snapshot;
                _marketWriteUtc = File.GetLastWriteTimeUtc(path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        MarketImported?.Invoke();
        return importedCount;
    }

    internal static (int ItemId, MarketSnapshot Snapshot) ParseLiveMarket(
        string text, IEnumerable<DatabaseItem> items)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        const string suffix = " - Live Market";
        if (lines.Length == 0 || !lines[0].EndsWith(suffix, StringComparison.Ordinal))
            throw new InvalidDataException("Missing Ragnarok+ Live Market title.");

        var name = lines[0][..^suffix.Length];
        var matches = items.Where(item => item.Id > 0 && string.Equals(item.Name, name,
            StringComparison.OrdinalIgnoreCase)).DistinctBy(item => item.Id).Take(2).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("The market item name must match one catalog item.");

        var itemId = matches[0].Id;
        var sellingCount = System.Text.RegularExpressions.Regex.Match(
            lines.ElementAtOrDefault(1) ?? "", @"^(?<count>[0-9]+) selling$");
        if (!sellingCount.Success || !int.TryParse(sellingCount.Groups["count"].Value, out var expected))
            throw new InvalidDataException("Missing selling offer count.");

        var rows = new List<MarketListing>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i] is not ("SC" or "HC")) continue;
            if (i + 4 >= lines.Length)
                throw new InvalidDataException("Incomplete market row.");
            var location = System.Text.RegularExpressions.Regex.Match(lines[i + 2],
                @"^(?<map>[A-Za-z0-9_-]+) \((?<x>[0-9]+), ?(?<y>[0-9]+)\)$");
            var quantity = lines[i + 3];
            var price = lines[i + 4];
            if (!location.Success || !int.TryParse(location.Groups["x"].Value, out var x) ||
                !int.TryParse(location.Groups["y"].Value, out var y) ||
                !System.Text.RegularExpressions.Regex.IsMatch(quantity, @"^x[0-9][0-9,]*$") ||
                !System.Text.RegularExpressions.Regex.IsMatch(price, @"^[0-9][0-9,]* ?z?$"))
                throw new InvalidDataException("Unexpected market row format.");
            rows.Add(new MarketListing
            {
                ItemId = itemId, ItemName = name, Owner = lines[i - 1], Stall = lines[i + 1],
                GameMode = lines[i], ShopType = "SELL", Map = location.Groups["map"].Value,
                X = x, Y = y, Quantity = quantity[1..], Price = price.TrimEnd('z', ' ')
            });
            i += 4;
        }
        if (rows.Count != expected)
            throw new InvalidDataException("The offer count does not match the copied market rows.");
        return (itemId, new MarketSnapshot { CapturedUtc = DateTimeOffset.UtcNow, Listings = rows });
    }

    internal sealed class MarketSnapshot
    {
        public DateTimeOffset CapturedUtc { get; set; }
        public required List<MarketListing> Listings { get; set; }
    }

    private static string CatalogPath => Path.Combine(AppEnvironment.LocalDirectory, "database.ragnarokplus.json.gz");

    public static GameDatabase LoadCatalog()
    {
        var bundled = Load<GameDatabase>(Path.Combine(AppContext.BaseDirectory, "Data", "database.ragnarokplus.json"));
        var local = Load<GameDatabase>(CatalogPath)
            ?? Load<GameDatabase>(Path.Combine(AppEnvironment.LocalDirectory, "database.ragnarokplus.json"));
        if (local is null) return bundled ?? new GameDatabase { Source = GameServerProfile.RagnarokPlus };
        if (bundled is null) return local;

        var localItems = local.Items.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        foreach (var seed in bundled.Items)
        {
            if (!localItems.TryGetValue(seed.Id, out var item))
            {
                if (!local.CatalogSynchronizedUtc.HasValue) local.Items.Add(seed);
                continue;
            }
            if (item.RagnarokPlusDetailsCaptured) continue;
            if (string.IsNullOrWhiteSpace(item.Description)) item.Description = seed.Description;
            item.LocalImagePng ??= seed.LocalImagePng;
            if (item.NpcSellPrice == 0) item.NpcSellPrice = seed.NpcSellPrice;
        }
        return local;
    }

    internal static void SaveCatalog(GameDatabase catalog)
    {
        Directory.CreateDirectory(AppEnvironment.LocalDirectory);
        var temporary = CatalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary))
            using (var compressed = new GZipStream(file, CompressionLevel.Fastest))
                JsonSerializer.Serialize(compressed, catalog, JsonOptions);
            File.Move(temporary, CatalogPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static MarketSnapshot? LoadMarket()
    {
        lock (MarketLock)
        {
            var path = MarketPath;
            var lastWriteUtc = File.GetLastWriteTimeUtc(path);
            if (lastWriteUtc != _marketWriteUtc)
            {
                _marketWriteUtc = lastWriteUtc;
                var snapshot = Load<MarketSnapshot>(path);
                _market = snapshot is { Listings: not null } && snapshot.CapturedUtc != default
                    ? snapshot : null;
            }
            return _market;
        }
    }

    internal static IReadOnlyList<MarketListing> Search(MarketSnapshot? snapshot, string query, string type) =>
        (snapshot?.Listings ?? [])
            .Where(row => row is not null && row.ShopType == type && row.ItemId > 0 &&
                (row.ItemId.ToString() == query || row.ItemName?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true))
            .Select(row => (Row: row, Price: SpringRoMarketService.ParsePrice(row.Price ?? "")))
            .Where(entry => entry.Price is > 0 and < long.MaxValue)
            .OrderBy(entry => type == "BUY" ? -entry.Price : entry.Price)
            .Take(100).Select(entry => entry.Row).ToArray();

    public static bool TryGetMinimumSellPrice(int itemId, string name, out long price)
    {
        price = (LoadMarket()?.Listings ?? [])
            .Where(row => row is not null && row.ShopType == "SELL" && row.GameMode == "SC" &&
                (itemId > 0 ? row.ItemId == itemId : string.Equals(row.ItemName, name, StringComparison.CurrentCultureIgnoreCase)))
            .Select(row => SpringRoMarketService.ParsePrice(row.Price ?? ""))
            .Where(value => value > 0)
            .DefaultIfEmpty(long.MaxValue).Min();
        if (price != long.MaxValue) return true;
        price = 0;
        return false;
    }

    private static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var file = File.OpenRead(path);
            if (!path.EndsWith(".gz", StringComparison.Ordinal))
                return JsonSerializer.Deserialize<T>(file, JsonOptions);
            using var compressed = new GZipStream(file, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<T>(compressed, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            DebugLog.Write($"RAGNAROK+ DATA ERROR: {ex.Message}");
            return null;
        }
    }
}
