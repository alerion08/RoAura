using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public sealed class FarmingSessionHistoryWindow : Window
{
    private readonly FarmingSessionHistoryStore _store;
    private readonly DataGrid _sessions = new() { AutoGenerateColumns = false, IsReadOnly = true,
        CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single };
    private readonly DataGrid _details = new() { AutoGenerateColumns = false, IsReadOnly = true,
        CanUserAddRows = false, CanUserDeleteRows = false };
    public FarmingSessionHistoryWindow(FarmingSessionHistoryStore store)
    {
        _store = store;
        Title = LocalizationService.Get("SessionHistory"); Width = 1050; Height = 730;
        MinWidth = 720; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "RoWindowBrush");
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var delete = new Button { Content = LocalizationService.Get("DeleteSession"), Margin = new Thickness(0,0,8,8) };
        delete.Click += (_, _) => DeleteSelected();
        var refresh = new Button { Content = LocalizationService.Get("HistoryRefresh"), Margin = new Thickness(0,0,0,8) };
        refresh.Click += (_, _) => RefreshRows();
        actions.Children.Add(delete); actions.Children.Add(refresh); root.Children.Add(actions);
        Add(_sessions, "HistoryDate", "DateText"); Add(_sessions, "SessionTime", "DurationText");
        Add(_sessions, "SessionItems", "ItemCount"); Add(_sessions, "ZenyTotalLabel", "ZenyGained");
        Add(_sessions, "AccountedLootValue", "LootValue"); Add(_sessions, "CharacterManager", "CharacterCaption");
        Grid.SetRow(_sessions, 1); root.Children.Add(_sessions);
        var note = new TextBlock { Text = LocalizationService.Get("SessionHistoryPriceHint"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,8) };
        Grid.SetRow(note, 2); root.Children.Add(note);
        Add(_details, "HistoryDate", "TimeText"); Add(_details, "Item", "Name"); Add(_details, "Count", "Count");
        Add(_details, "HistoryUnitPrice", "UnitPrice"); Add(_details, "PriceSource", "PriceSource"); Add(_details, "Total", "Total");
        Grid.SetRow(_details, 3); root.Children.Add(_details);
        _sessions.SelectionChanged += (_, _) => _details.ItemsSource = (_sessions.SelectedItem as FarmingSession)?.Loot;
        Content = root;
        Loaded += (_, _) => RefreshRows();
    }
    private static void Add(DataGrid grid, string key, string path) =>
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get(key),
            Binding = new System.Windows.Data.Binding(path), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
    private void RefreshRows()
    {
        try { _sessions.ItemsSource = _store.List(); }
        catch (Exception ex) { MessageBox.Show(this, LocalizationService.Get("SessionHistoryError") + "\n" + ex.Message); }
    }
    private void DeleteSelected()
    {
        if (_sessions.SelectedItem is not FarmingSession session) return;
        if (MessageBox.Show(this, LocalizationService.Get("DeleteSessionConfirm"), Title,
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { _store.Delete(session.Id); RefreshRows(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
