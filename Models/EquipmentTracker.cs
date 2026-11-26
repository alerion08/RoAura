using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using RoAura.Services;

namespace RoAura.Models;

public sealed class EquipmentTracker : INotifyPropertyChanged
{
    public CharacterScope CharacterScope { get; set; } = new();

    private string _name = "New equipment";
    private bool _enabled = true;
    private int _itemId;
    private int _inventoryIndex;
    private int _wearLocation;
    private int _packetId = 0x00AA;
    private string _variantKey = "";
    private string _detectedItemName = "";
    private string _iconImageBase64 = "";
    private string _iconFileName = "";
    private bool _useDatabaseIcon;
    private Func<int, string?>? _resolveDatabaseIcon;
    private string? _databaseIconImageBase64;
    private string _text = "EQUIPMENT";
    private int _groupNumber = 1;
    private double _x = 1100;
    private double _y = 420;
    private double _iconSize = 56;
    private double _iconPadding;
    private double _textSize = 16;
    private double _opacityPercent = 100;
    private string _textColor = "#FFFFFFFF";
    private string _backgroundColor = "#D911141B";
    private string _borderColor = "#B8C5CBD4";
    private bool _showText = true;
    private bool _showTextOnIcon;
    private double _textBackgroundOpacityPercent = 65;
    private bool _showBackground = true;
    private bool _showBorder = true;
    private bool _isEquipped;
    private bool _isPlacementPreview;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value); }
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) OnPropertyChanged(nameof(IsVisible)); } }
    public int ItemId
    {
        get => _itemId;
        set
        {
            if (!Set(ref _itemId, Math.Clamp(value, 0, int.MaxValue))) return;
            OnPropertyChanged(nameof(CurrentStateText));
            RefreshDatabaseIcon();
        }
    }
    public int InventoryIndex { get => _inventoryIndex; set => Set(ref _inventoryIndex, Math.Clamp(value, 0, ushort.MaxValue)); }
    public int WearLocation { get => _wearLocation; set => Set(ref _wearLocation, Math.Clamp(value, 0, int.MaxValue)); }
    public int PacketId
    {
        get => _packetId;
        set { if (Set(ref _packetId, Math.Clamp(value, 0, ushort.MaxValue))) OnPropertyChanged(nameof(PacketIdHex)); }
    }
    public string VariantKey
    {
        get => _variantKey;
        set
        {
            if (Set(ref _variantKey, value ?? ""))
                OnPropertyChanged(nameof(CurrentStateText));
        }
    }
    public string DetectedItemName { get => _detectedItemName; set => Set(ref _detectedItemName, value ?? ""); }
    public string IconImageBase64
    {
        get => _iconImageBase64;
        set
        {
            if (Set(ref _iconImageBase64, value)) NotifyEffectiveIconChanged();
        }
    }
    public bool UseDatabaseIcon
    {
        get => _useDatabaseIcon;
        set
        {
            if (Set(ref _useDatabaseIcon, value)) RefreshDatabaseIcon();
        }
    }

    [JsonIgnore] public string EffectiveIconImageBase64 => _databaseIconImageBase64 ?? IconImageBase64;

    internal void SetDatabaseIconResolver(Func<int, string?> resolver)
    {
        _resolveDatabaseIcon = resolver;
        RefreshDatabaseIcon();
    }

    private void RefreshDatabaseIcon()
    {
        var icon = UseDatabaseIcon && ItemId > 0 ? _resolveDatabaseIcon?.Invoke(ItemId) : null;
        if (_databaseIconImageBase64 == icon) return;
        _databaseIconImageBase64 = icon;
        NotifyEffectiveIconChanged();
    }

    private void NotifyEffectiveIconChanged()
    {
        OnPropertyChanged(nameof(EffectiveIconImageBase64));
        OnPropertyChanged(nameof(IconStatusText));
    }

    public string IconFileName { get => _iconFileName; set { if (Set(ref _iconFileName, value)) OnPropertyChanged(nameof(IconStatusText)); } }
    public string Text { get => _text; set => Set(ref _text, value); }
    public int GroupNumber { get => _groupNumber; set => Set(ref _groupNumber, Math.Clamp(value, 0, 999)); }
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double IconSize
    {
        get => _iconSize;
        set { if (Set(ref _iconSize, Math.Clamp(value, 20, 180))) OnPropertyChanged(nameof(OverlayIconSize)); }
    }
    private double? _groupIconSize;
    [JsonIgnore] public double OverlayIconSize => _groupIconSize ?? IconSize;

    internal void SetGroupIconSize(double? size)
    {
        if (_groupIconSize == size) return;
        _groupIconSize = size;
        OnPropertyChanged(nameof(OverlayIconSize));
    }
    public double IconPadding { get => _iconPadding; set => Set(ref _iconPadding, Math.Clamp(value, 0, 40)); }
    public double TextSize { get => _textSize; set => Set(ref _textSize, Math.Clamp(value, 10, 96)); }
    public double OpacityPercent { get => _opacityPercent; set { if (Set(ref _opacityPercent, Math.Clamp(value, 0, 100))) OnPropertyChanged(nameof(Opacity)); } }
    public string TextColor { get => _textColor; set => Set(ref _textColor, value); }
    public string BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }
    public string BorderColor { get => _borderColor; set => Set(ref _borderColor, value); }
    public bool ShowText
    {
        get => _showText;
        set { if (Set(ref _showText, value)) NotifyTextPositionChanged(); }
    }

    public bool ShowTextOnIcon
    {
        get => _showTextOnIcon;
        set { if (Set(ref _showTextOnIcon, value)) NotifyTextPositionChanged(); }
    }

    public double TextBackgroundOpacityPercent
    {
        get => _textBackgroundOpacityPercent;
        set => Set(ref _textBackgroundOpacityPercent, Math.Clamp(value, 0, 100));
    }

    [JsonIgnore] public bool ShowTextBelowIcon => ShowText && !ShowTextOnIcon;
    [JsonIgnore] public bool ShowTextOverIcon => ShowText && ShowTextOnIcon;

    private void NotifyTextPositionChanged()
    {
        OnPropertyChanged(nameof(ShowTextBelowIcon));
        OnPropertyChanged(nameof(ShowTextOverIcon));
    }

    public bool ShowBackground { get => _showBackground; set => Set(ref _showBackground, value); }
    public bool ShowBorder { get => _showBorder; set => Set(ref _showBorder, value); }

    [JsonIgnore]
    public bool IsEquipped { get => _isEquipped; set { if (Set(ref _isEquipped, value)) { OnPropertyChanged(nameof(IsVisible)); OnPropertyChanged(nameof(CurrentStateText)); } } }

    [JsonIgnore]
    public bool IsPlacementPreview { get => _isPlacementPreview; set { if (Set(ref _isPlacementPreview, value)) OnPropertyChanged(nameof(IsVisible)); } }

    [JsonIgnore] public bool IsVisible => Enabled && (IsEquipped || IsPlacementPreview);
    [JsonIgnore] public double Opacity => OpacityPercent / 100d;
    [JsonIgnore] public string PacketIdHex => $"0x{PacketId:X4}";
    [JsonIgnore] public string CurrentStateText => LocalizationService.Get(
        ItemId <= 0 && string.IsNullOrWhiteSpace(VariantKey)
            ? "NoEquipmentData"
            : IsEquipped ? "Equipped" : "NotEquipped");
    [JsonIgnore] public string IconStatusText => _databaseIconImageBase64 is not null
        ? LocalizationService.Get("UseEquipmentDatabaseIcon")
        : string.IsNullOrWhiteSpace(IconImageBase64)
        ? LocalizationService.Get("NotSelected")
        : string.IsNullOrWhiteSpace(IconFileName) ? LocalizationService.Get("Loaded") : IconFileName;

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
