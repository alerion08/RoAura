using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RoAura.Models;

namespace RoAura;

/// <summary>Shares item templates across groups while giving each group its own layer on the overlay surface.</summary>
public sealed class OverlayLayerSource : ItemsControl
{
    public static readonly DependencyProperty LayerSourceProperty =
        DependencyProperty.Register(nameof(LayerSource), typeof(IEnumerable), typeof(OverlayLayerSource),
            new PropertyMetadata(null, (owner, args) => ((OverlayLayerSource)owner).ChangeSource(
                args.OldValue as INotifyCollectionChanged, args.NewValue as INotifyCollectionChanged)));

    public IEnumerable? LayerSource
    {
        get => (IEnumerable?)GetValue(LayerSourceProperty);
        set => SetValue(LayerSourceProperty, value);
    }

    public string LayerPrefix { get; set; } = "";
    private readonly Dictionary<int, (ItemsControl View, ObservableCollection<object> Rows)> _layers = [];
    private readonly HashSet<INotifyPropertyChanged> _observed = [];
    private Grid? _surface;
    private AppSettings? _settings;

    public OverlayLayerSource() => IsVisibleChanged += (_, _) => ApplyPreferences();

    private void ChangeSource(INotifyCollectionChanged? previous, INotifyCollectionChanged? current)
    {
        if (previous is not null) previous.CollectionChanged -= SourceChanged;
        if (current is not null) current.CollectionChanged += SourceChanged;
        SyncGroups();
    }

    private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs args) => SyncGroups();

    private void RowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is "GroupNumber" or null or "") SyncGroups();
    }

    internal void RefreshLayers(Grid surface, AppSettings settings)
    {
        if (_surface is null)
        {
            _surface = surface;
            _settings = settings;
            SyncGroups();
        }
        else ApplyPreferences();
    }

    private void SyncGroups()
    {
        if (_surface is null) return;
        var rows = LayerSource?.Cast<object>().ToArray() ?? [];
        var observed = rows.OfType<INotifyPropertyChanged>().ToHashSet();
        foreach (var row in _observed.Except(observed)) row.PropertyChanged -= RowChanged;
        foreach (var row in observed.Except(_observed)) row.PropertyChanged += RowChanged;
        _observed.Clear();
        _observed.UnionWith(observed);

        var groups = rows.GroupBy(GroupNumber).ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var missing in _layers.Keys.Except(groups.Keys).ToArray())
        {
            _surface.Children.Remove(_layers[missing].View);
            _layers.Remove(missing);
        }
        foreach (var (number, groupRows) in groups)
        {
            if (!_layers.TryGetValue(number, out var layer))
            {
                var members = new ObservableCollection<object>();
                var view = new ItemsControl
                {
                    ItemsPanel = ItemsPanel, ItemContainerStyle = ItemContainerStyle,
                    ItemTemplate = ItemTemplate, ItemsSource = members
                };
                _layers.Add(number, layer = (view, members));
                _surface.Children.Add(view);
            }
            for (var index = 0; index < groupRows.Length; index++)
            {
                var current = layer.Rows.IndexOf(groupRows[index]);
                if (current < 0) layer.Rows.Insert(index, groupRows[index]);
                else if (current != index) layer.Rows.Move(current, index);
            }
            while (layer.Rows.Count > groupRows.Length) layer.Rows.RemoveAt(layer.Rows.Count - 1);
        }
        ApplyPreferences();
    }

    private void ApplyPreferences()
    {
        var baseline = LayerPrefix switch
        {
            "cooldown" => -9, "buff" => -8, "reminder" => -7, "equipment" => -6,
            "effect" => -5, "reward" => -4, "statistics" => -2, _ => 0
        };
        foreach (var (number, layer) in _layers)
        {
            var preference = _settings?.OverlayPanels.GetValueOrDefault($"{LayerPrefix}:{number}");
            layer.View.Visibility = IsVisible && preference?.Hidden != true ? Visibility.Visible : Visibility.Collapsed;
            Panel.SetZIndex(layer.View, preference is { Layer: not 0 } ? preference.Layer : baseline);
        }
    }

    private static int GroupNumber(object row) => row switch
    {
        SkillAura value => value.GroupNumber,
        BuffPanelGroup value => value.GroupNumber,
        ReminderItem value => value.GroupNumber,
        EquipmentTracker value => value.GroupNumber,
        EffectNotification value => value.GroupNumber,
        RewardGroupSettings value => value.GroupNumber,
        RateIndicatorSettings value => value.GroupNumber,
        _ => 0
    };
}
