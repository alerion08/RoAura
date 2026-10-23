using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

public sealed class GoldGoblinService
{
    private readonly AppSettings _settings;
    private readonly SpringRoMarketService _market;
    private readonly Dictionary<int, SpringRoItemPrice> _prices;
    public ObservableCollection<GoldGoblinLootRow> Rows { get; } = [];
    public event Action? Changed;
    public long SessionTotal => Rows.Sum(row => row.Total);
    public DateTime? SessionStartedUtc { get; private set; }

    public GoldGoblinService(AppSettings settings, SpringRoMarketService market)
    {
        _settings = settings;
        _market = market;
        _prices = LoadPrices();
        foreach (var rule in settings.GoldGoblinPriceRules) Watch(rule);
        settings.GoldGoblinPriceRules.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
                foreach (GoldGoblinPriceRule rule in args.NewItems) Watch(rule);
            Reprice();
        };
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppSettings.GoldGoblinOverchargeLevel)) Reprice();
        };
        market.DataUpdated += Reprice;
    }

    public void AddLoot(int itemId, string name, long count)
    {
        if (count <= 0) return;
        SessionStartedUtc ??= DateTime.UtcNow;
        var row = Rows.FirstOrDefault(item => item.ItemId == itemId);
        if (row is null)
        {
            row = new GoldGoblinLootRow { ItemId = itemId, Item = name };
            row.SetCalculatedCost(ResolveUnitPrice(itemId, name));
            Watch(row);
            Rows.Add(row);
        }
        row.Count += count;
        Changed?.Invoke();
    }

    public long ResolveUnitPrice(int itemId, string name)
    {
        var rule = _settings.GoldGoblinPriceRules.FirstOrDefault(item =>
            (item.ItemId > 0 && item.ItemId == itemId) ||
            string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (rule is not null)
        {
            if (rule.UseVendingPrice && _market.TryGetMinimumSellPrice(itemId, name, out var vending))
                return vending;
            if (rule.ManualPrice > 0) return rule.ManualPrice;
        }

        var npc = _prices.GetValueOrDefault(itemId)?.NpcSellPrice ?? 0;
        return ApplyOvercharge(npc, _settings.GoldGoblinOverchargeLevel);
    }

    public long LootValue(int itemId, string name, long count) =>
        checked(ResolveUnitPrice(itemId, name) * Math.Max(0, count));

    public void Reset()
    {
        Rows.Clear();
        SessionStartedUtc = null;
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
            row.SetCalculatedCost(ResolveUnitPrice(row.ItemId, row.Item));
        Changed?.Invoke();
    }

    public static long ApplyOvercharge(long value, int level)
    {
        if (value <= 0 || level <= 0) return Math.Max(0, value);
        var percent = level >= 10 ? 24 : 5 + level * 2;
        return (long)Math.Floor(value * (100 + percent) / 100d);
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
            var data = JsonSerializer.Deserialize<SpringRoItemPriceDatabase>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return data?.Items.ToDictionary(item => item.Id) ?? [];
        }
        catch (Exception ex)
        {
            DebugLog.Write($"GOLD GOBLIN PRICE DATABASE ERROR: {ex.Message}");
            return [];
        }
    }
}
