using System.ComponentModel;
using System.Runtime.CompilerServices;
using RoAura.Services;

namespace RoAura.Models;

public enum RewardKind
{
    Loot,
    Zeny,
    Experience
}

public sealed class RewardChannelSettings : INotifyPropertyChanged
{
    private bool _enabled = true;
    private int _groupNumber = 1;
    private int _order = 1;
    private string _textColor = "#FFFFFFFF";
    private string _equipColor = "#FF9CCBFF";
    private string _cardColor = "#FFFF8AD8";
    private string _lootColor = "#FFFFD45C";
    private string _consumableColor = "#FF7FE39A";

    public RewardKind Kind { get; set; }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public int GroupNumber { get => _groupNumber; set => Set(ref _groupNumber, Math.Clamp(value, 1, 99)); }
    public int Order { get => _order; set => Set(ref _order, Math.Clamp(value, 1, 99)); }
    public string TextColor { get => _textColor; set => Set(ref _textColor, value ?? "#FFFFFFFF"); }
    public string EquipColor { get => _equipColor; set => Set(ref _equipColor, value ?? "#FF9CCBFF"); }
    public string CardColor { get => _cardColor; set => Set(ref _cardColor, value ?? "#FFFF8AD8"); }
    public string LootColor { get => _lootColor; set => Set(ref _lootColor, value ?? "#FFFFD45C"); }
    public string ConsumableColor { get => _consumableColor; set => Set(ref _consumableColor, value ?? "#FF7FE39A"); }

    public string ColorForItemType(string? type) => type switch
    {
        "Equip" => EquipColor,
        "Card" => CardColor,
        "Consumable" => ConsumableColor,
        _ => LootColor
    };
    public string DisplayName => Kind switch
    {
        RewardKind.Loot => "Loot",
        RewardKind.Zeny => "Zeny",
        _ => "Experience"
    };
    public string LocalizedDisplayName => LocalizationService.Get(Kind switch
    {
        RewardKind.Loot => "LootChannelLabel",
        RewardKind.Zeny => "ZenyChannelLabel",
        _ => "ExperienceChannelLabel"
    });

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalizedDisplayName)));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
