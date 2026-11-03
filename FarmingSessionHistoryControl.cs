using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public sealed class FarmingSessionHistoryControl : UserControl
{
    private FarmingSessionHistoryStore Store => ((App)Application.Current).FarmingHistory;
    private readonly DataGrid _sessions = new() { AutoGenerateColumns = false, IsReadOnly = true,
        CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single };
    private readonly Button _open = new() { IsEnabled = false };
    public FarmingSessionHistoryControl()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var actions = new WrapPanel { Margin = new Thickness(0,0,0,8) };
        _open.SetResourceReference(ContentControl.ContentProperty, "OpenSessionDetails");
        _open.Click += (_, _) => OpenSelected();
        var delete = new Button(); delete.SetResourceReference(ContentControl.ContentProperty, "DeleteSession");
        delete.Click += (_, _) => DeleteSelected();
        var refresh = new Button(); refresh.SetResourceReference(ContentControl.ContentProperty, "HistoryRefresh");
        refresh.Click += (_, _) => RefreshRows();
        actions.Children.Add(_open); actions.Children.Add(delete); actions.Children.Add(refresh); root.Children.Add(actions);
        AddColumn(_sessions, "HistoryDate", "DateText"); AddColumn(_sessions, "SessionTime", "DurationText");
        AddColumn(_sessions, "Item", "ItemNames"); AddColumn(_sessions, "SessionItems", "ItemCount"); AddColumn(_sessions, "ZenyTotalLabel", "ZenyGained");
        AddColumn(_sessions, "AccountedLootValue", "LootValue"); AddColumn(_sessions, "CharacterLabel", "CharacterCaption");
        Grid.SetRow(_sessions, 1); root.Children.Add(_sessions);
        _sessions.SelectionChanged += (_, _) => _open.IsEnabled = _sessions.SelectedItem is FarmingSession;
        _sessions.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(_sessions, source) is DataGridRow) OpenSelected();
        };
        Content = root; Loaded += (_, _) => RefreshRows();
    }
    internal static void AddColumn(DataGrid grid, string key, string path)
    {
        var column = new DataGridTextColumn { Header = LocalizationService.Get(key),
            Binding = new Binding(path), Width = new DataGridLength(path is "CharacterCaption" or "Name" or "ItemNames" ? 1.7 : 1, DataGridLengthUnitType.Star) };
        if (path == "ItemNames")
        {
            column.MinWidth = 140;
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(path)));
            column.ElementStyle = style;
        }
        grid.Columns.Add(column);
    }
    private void OpenSelected()
    {
        if (_sessions.SelectedItem is not FarmingSession session) return;
        var details = new FarmingSessionDetailsWindow(session) { Owner = Window.GetWindow(this) };
        details.ShowDialog();
    }
    private void RefreshRows()
    {
        try
        {
            var selected = (_sessions.SelectedItem as FarmingSession)?.Id;
            if (!((App)Application.Current).FlushFarmingHistory()) return;
            var rows = Store.List(); _sessions.ItemsSource = rows;
            _sessions.SelectedItem = rows.FirstOrDefault(row => row.Id == selected);
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), LocalizationService.Get("SessionHistoryError") + "\n" + ex.Message); }
    }
    private void DeleteSelected()
    {
        if (_sessions.SelectedItem is not FarmingSession session) return;
        if (MessageBox.Show(Window.GetWindow(this), LocalizationService.Get("DeleteSessionConfirm"), LocalizationService.Get("SessionHistory"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { Store.Delete(session.Id); RefreshRows(); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, LocalizationService.Get("SessionHistory"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
