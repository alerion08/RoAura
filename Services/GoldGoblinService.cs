using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Threading;
using RoAura.Models;

namespace RoAura.Services;

public sealed class GoldGoblinService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly AppSettings _settings;
    private readonly SpringRoMarketService _market;
    private readonly Dictionary<int, SpringRoItemPrice> _prices;
    // Rows is bound to the UI; market updates arrive on thread-pool threads and must
    // be marshalled before touching it.
    private readonly Dispatcher? _dispatcher;
    public ObservableCollection<GoldGoblinLootRow> Rows { get; } = [];
    public event Action? Changed;
    public long SessionTotal => Rows.Sum(row => row.Total);
    public DateTime? SessionStartedUtc { get; private set; }

    public GoldGoblinService(AppSettings settings, SpringRoMarketService market)
    {
        _settings = settings;
        _market = market;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.FromThread(Thread.CurrentThread);
        _prices = LoadPrices();
        foreach (var rule in settings.GoldGoblinPriceRules) Watch(rule);
        settings.GoldGoblinPriceRules.CollectionChanged += (_, args) =>
        {
            if (args.OldItems is not null)
                foreach (GoldGoblinPriceRule rule in args.OldItems) rule.PropertyChanged -= Rule_PropertyChanged;
            if (args.NewItems is not null)
                foreach (GoldGoblinPriceRule rule in args.NewItems) Watch(rule);
            Reprice();
        };
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppSettings.GoldGoblinOverchargeLevel)) Reprice();
        };
        market.DataUpdated += Market_DataUpdated;
    }

    public void AddLoot(int itemId, string name, long count)
    {
        if (count <= 0) return;
        SessionStartedUtc ??= DateTime.UtcNow;
        var row = Rows.FirstOrDefault(item => item.ItemId == itemId);
        if (row is null)
        {
            row = new GoldGoblinLootRow { ItemId = itemId, Item = name };
            var price = ResolvePriceInfo(itemId, name);
            row.SetCalculatedCost(price.Value, price.Source, price.Known);
            Watch(row);
            Rows.Add(row);
        }
        row.Count += count;
        Changed?.Invoke();
    }

    public long ResolveUnitPrice(int itemId, string name)
        => ResolvePriceInfo(itemId, name).Value;

    public (long Value, string Source, bool Known) ResolvePriceInfo(int itemId, string name)
    {
        // A rule bound to an item ID matches that ID only; a rule without an ID matches by name.
        var rule = _settings.GoldGoblinPriceRules.FirstOrDefault(item =>
            item.ItemId > 0
                ? item.ItemId == itemId
                : string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (rule is not null)
        {
            if (rule.UseVendingPrice && _market.TryGetMinimumSellPrice(itemId, name, out var vending))
                return (vending, "Vending", true);
            if (rule.ManualPrice > 0) return (rule.ManualPrice, "Manual", true);
        }

        if (_prices.TryGetValue(itemId, out var itemPrice))
            return (ApplyOvercharge(itemPrice.NpcSellPrice, _settings.GoldGoblinOverchargeLevel), "NPC", true);
        return (0, "—", false);
    }

    public long LootValue(int itemId, string name, long count) =>
        checked(ResolveUnitPrice(itemId, name) * Math.Max(0, count));

    public void Reset()
    {
        Rows.Clear();
        SessionStartedUtc = null;
        Changed?.Invoke();
    }

    public void StartSession()
    {
        Rows.Clear();
        SessionStartedUtc = DateTime.UtcNow;
        Changed?.Invoke();
    }

    public void AddManualItem(int itemId, string name) => AddLoot(itemId, name, 1);

    public void Remove(GoldGoblinLootRow row)
    {
        Rows.Remove(row);
        Changed?.Invoke();
    }

    public void Reprice()
    {
        foreach (var row in Rows.Where(item => !item.HasManualCost))
        {
            var price = ResolvePriceInfo(row.ItemId, row.Item);
            row.SetCalculatedCost(price.Value, price.Source, price.Known);
        }
        Changed?.Invoke();
    }

    public static long ApplyOvercharge(long value, int level)
    {
        if (value <= 0 || level <= 0) return Math.Max(0, value);
        var percent = level >= 10 ? 24 : 5 + level * 2;
        return (long)Math.Floor(value * (100 + percent) / 100d);
    }

    private void Market_DataUpdated()
    {
        if (_dispatcher is null || _dispatcher.CheckAccess()) Reprice();
        else _dispatcher.InvokeAsync(Reprice);
    }

    private void Watch(GoldGoblinPriceRule rule) =>
        rule.PropertyChanged += Rule_PropertyChanged;
    private void Watch(GoldGoblinLootRow row) =>
        row.PropertyChanged += (_, _) => Changed?.Invoke();
    private void Rule_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Reprice();

    private static Dictionary<int, SpringRoItemPrice> LoadPrices()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "item-prices.springro.json");
            var data = JsonSerializer.Deserialize<SpringRoItemPriceDatabase>(File.ReadAllText(path), JsonOptions);
            if (data is null) return [];
            var groups = data.Items.GroupBy(item => item.Id).ToList();
            var duplicates = groups.Where(group => group.Count() > 1).Select(group => group.Key).ToList();
            if (duplicates.Count > 0)
                DebugLog.Write($"GOLD GOBLIN PRICE DATABASE: {duplicates.Count} duplicate item IDs ignored (first wins): {string.Join(", ", duplicates.Take(20))}");
            return groups.ToDictionary(group => group.Key, group => group.First());
        }
        catch (Exception ex)
        {
            DebugLog.Write($"GOLD GOBLIN PRICE DATABASE ERROR: {ex}");
            return [];
        }
    }
}
