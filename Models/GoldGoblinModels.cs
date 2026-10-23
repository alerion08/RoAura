using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RoAura.Models;

public sealed class GoldGoblinPriceRule : INotifyPropertyChanged
{
    private string _name = "";
    private int _itemId;
    private bool _useVendingPrice;
    private long _manualPrice;

    public int ItemId { get => _itemId; set => Set(ref _itemId, Math.Max(0, value)); }
    public string Name { get => _name; set => Set(ref _name, value?.Trim() ?? ""); }
    public bool UseVendingPrice { get => _useVendingPrice; set => Set(ref _useVendingPrice, value); }
    public long ManualPrice { get => _manualPrice; set => Set(ref _manualPrice, Math.Max(0, value)); }

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
    private long _cost;
    public int ItemId { get; init; }
    public string Item { get; init; } = "";
    public long Count { get => _count; set { _count = value; Changed(); Changed(nameof(Total)); } }
    public long Cost { get => _cost; set { _cost = value; Changed(); Changed(nameof(Total)); } }
    public long Total => checked(Count * Cost);
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
