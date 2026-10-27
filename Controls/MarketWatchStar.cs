using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Models;
using RoAura.Services;

namespace RoAura.Controls;

public sealed class MarketWatchStar : Button
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(object), typeof(MarketWatchStar),
        new PropertyMetadata(null, (d, _) => ((MarketWatchStar)d).Refresh()));
    public object? Item { get => GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    private AppSettings? _settings;
    public MarketWatchStar()
    {
        Content = "★"; FontSize = 22; Width = 32; Height = 32; MinHeight = 0;
        Padding = new Thickness(0); Margin = new Thickness(5, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Top;
        Click += (_, e) => { e.Handled = true; (Application.Current as App)?.ToggleMarketWatch(Window.GetWindow(this), Item); Refresh(); };
        Loaded += (_, _) =>
        {
            Detach();
            _settings = (Application.Current as App)?.MarketWatchSettings;
            if (_settings is not null) _settings.MarketWatchRules.CollectionChanged += RulesChanged;
            Refresh();
        };
        Unloaded += (_, _) => Detach();
    }
    private void Detach()
    {
        if (_settings is not null) _settings.MarketWatchRules.CollectionChanged -= RulesChanged;
        _settings = null;
    }
    private void RulesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        var id = Item switch { DatabaseItem i => i.Id, MarketListing l => l.ItemId, _ => 0 };
        Visibility = id > 0 ? Visibility.Visible : Visibility.Collapsed;
        var active = _settings?.MarketWatchRules.Any(r => r.ItemId == id) == true;
        Opacity = active ? 1 : .4;
        Foreground = new SolidColorBrush(Color.FromRgb(15, 48, 102));
        ToolTip = LocalizationService.Get(active ? "WatchStop" : "WatchAddTitle");
    }
}
