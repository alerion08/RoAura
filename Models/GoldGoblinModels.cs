using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RoAura.Models;

public sealed record ManualPriceChange(DateTime ChangedUtc, long PreviousPrice, long NewPrice);

public sealed class GoldGoblinPriceRule : INotifyPropertyChanged
{
    private string _name = "";
    private int _itemId;
    private bool _useVendingPrice;
    private long _manualPrice;
    private bool _exclude;
    public bool Exclude { get => _exclude; set => Set(ref _exclude, value); }

    public int ItemId { get => _itemId; set => Set(ref _itemId, Math.Max(0, value)); }
    public string Name { get => _name; set => Set(ref _name, value?.Trim() ?? ""); }
    public bool UseVendingPrice { get => _useVendingPrice; set => Set(ref _useVendingPrice, value); }
    public long ManualPrice { get => _manualPrice; set => Set(ref _manualPrice, Math.Max(0, value)); }

    public List<ManualPriceChange> ManualPriceHistory { get; set; } = [];

    public void ChangeManualPrice(long price)
    {
        if (price < 0 || price == ManualPrice) return;
        ManualPriceHistory ??= [];
        ManualPriceHistory.Add(new(DateTime.UtcNow, ManualPrice, price));
        ManualPrice = price;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class GoldGoblinLootRow : INotifyPropertyChanged
{
    private long _count;
    private long _stolenCount;
    private long _cost;
    private bool _hasManualCost;
    private string _priceSource = "—";
    private bool _priceKnown;
    public int ItemId { get; init; }
    public string Item { get; init; } = "";
    public long Count
    {
        get => _count;
        set
        {
            _count = Math.Max(0, value);
            _stolenCount = Math.Min(_stolenCount, _count);
            Changed();
            NotifyTotals();
        }
    }
    public long StolenCount => _stolenCount;
    public long RegularCount => Count - StolenCount;
    public long StolenRevenue => Excluded ? 0 : checked(StolenCount * Cost);
    public long RegularRevenue => Total - StolenRevenue;

    internal void SetCounts(long count, long stolenCount)
    {
        _stolenCount = Math.Max(0, stolenCount);
        Count = count;
    }

    private void NotifyTotals()
    {
        Changed(nameof(StolenCount));
        Changed(nameof(RegularCount));
        Changed(nameof(StolenRevenue));
        Changed(nameof(RegularRevenue));
        Changed(nameof(Total));
    }
    public long Cost
    {
        get => _cost;
        set { _cost = Math.Max(0, value); _hasManualCost = true; PriceSource = "Manual"; PriceKnown = true; Changed(); NotifyTotals(); }
    }
    private bool _excluded;
    public bool Excluded { get => _excluded; set { _excluded = value; Changed(); NotifyTotals(); } }
    public long Total => Excluded ? 0 : checked(Count * Cost);
    public bool HasManualCost => _hasManualCost;
    public string PriceSource { get => _priceSource; private set { _priceSource = value; Changed(); } }
    public bool PriceKnown { get => _priceKnown; private set { _priceKnown = value; Changed(); } }
    public void SetCalculatedCost(long value, string source = "—", bool known = false)
    {
        _cost = Math.Max(0, value);
        PriceSource = source;
        PriceKnown = known;
        Changed(nameof(Cost));
        NotifyTotals();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SpringRoItemPrice
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long NpcSellPrice { get; set; }
}

public sealed class SpringRoItemPriceDatabase
{
    public string Source { get; set; } = "SpringRO";
    public string GeneratedUtc { get; set; } = "";
    public List<SpringRoItemPrice> Items { get; set; } = [];
}
