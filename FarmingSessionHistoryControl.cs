using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public sealed class FarmingSessionHistoryControl : UserControl
{
    private FarmingSessionHistoryStore Store => ((App)Application.Current).FarmingHistory;
    private readonly DataGrid _sessions = new() { AutoGenerateColumns = false, IsReadOnly = true,
        CanUserAddRows = false, CanUserDeleteRows = false, CanUserResizeRows = false, RowHeight = 30,
        SelectionMode = DataGridSelectionMode.Extended };
    private readonly Button _open = new() { IsEnabled = false };
    private readonly DatePicker _from = new() { Width = 142, SelectedDateFormat = DatePickerFormat.Short };
    private readonly DatePicker _through = new() { Width = 142, SelectedDateFormat = DatePickerFormat.Short };
    private readonly TextBox _item = new() { Width = 180 };
    private readonly TextBox _minimumMinutes = new() { Width = 105 };
    private readonly TextBox _tag = new() { Width = 120 };
    private readonly TextBlock _filterError = new() { Visibility = Visibility.Collapsed };
    private readonly UniformGrid _cards = new() { Margin = new Thickness(-3, 8, -3, 0) };
    private IReadOnlyList<FarmingSession> _history = [];

    public FarmingSessionHistoryControl()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var actions = CreateFilters();
        _open.SetResourceReference(ContentControl.ContentProperty, "OpenSessionDetails");
        _open.Click += (_, _) => OpenSelected();
        var compare = new Button { IsEnabled = false, ToolTip = LocalizationService.Get("CompareSessionsHint") };
        compare.SetResourceReference(ContentControl.ContentProperty, "CompareSessions");
        compare.Click += (_, _) => CompareSelected();
        var delete = new Button { IsEnabled = false }; delete.SetResourceReference(ContentControl.ContentProperty, "Delete");
        delete.Click += (_, _) => DeleteSelected();
        foreach (var button in new[] { _open, compare, delete })
        {
            button.Height = 32;
            button.Margin = new Thickness(0, 0, 6, 0);
            button.VerticalAlignment = VerticalAlignment.Bottom;
            actions.Children.Add(button);
        }

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new ScrollViewer
        {
            Content = actions, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        header.Children.Add(_filterError);
        header.Children.Add(_cards);
        _cards.SizeChanged += (_, _) => UpdateCardColumns();
        root.Children.Add(header);
        AddColumn(_sessions, "HistoryDate", "DateText");
        _sessions.Columns[0].MinWidth = 150;
        ((DataGridTextColumn)_sessions.Columns[0]).ElementStyle = new Style(typeof(TextBlock))
        {
            Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap),
                new Setter(VerticalAlignmentProperty, VerticalAlignment.Center) }
        };
        AddColumn(_sessions, "SessionTime", "DurationText");
        AddColumn(_sessions, "SessionItems", "ItemCount");
        AddColumn(_sessions, "AccountedLootValue", "LootValue"); AddColumn(_sessions, "CharacterLabel", "CharacterCaption");
        Grid.SetRow(_sessions, 1); root.Children.Add(_sessions);
        _sessions.SelectionChanged += (_, _) =>
        {
            _open.IsEnabled = delete.IsEnabled = _sessions.SelectedItems.Count == 1;
            compare.IsEnabled = _sessions.SelectedItems.Count == 2;
        };
        _sessions.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(_sessions, source) is DataGridRow) OpenSelected();
        };
        Content = root; Loaded += (_, _) => RefreshRows();
    }
    private StackPanel CreateFilters()
    {
        var filters = new StackPanel { Orientation = Orientation.Horizontal };
        AddFilter("HistoryFrom", _from);
        AddFilter("HistoryThrough", _through);
        AddFilter("HistoryItemFilter", _item);
        AddFilter("HistoryMinDuration", _minimumMinutes);
        AddFilter("HistoryTag", _tag);
        _minimumMinutes.TextChanged += (_, _) => ApplyFilters();
        _tag.TextChanged += (_, _) => ApplyFilters();
        _from.SelectedDateChanged += (_, _) => ApplyFilters();
        _through.SelectedDateChanged += (_, _) => ApplyFilters();
        _item.TextChanged += (_, _) => ApplyFilters();
        return filters;

        void AddFilter(string key, Control input)
        {
            var field = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            input.Height = 32;
            if (input is DatePicker picker)
                picker.SetResourceReference(StyleProperty, "HistoryDatePickerStyle");
            input.VerticalContentAlignment = VerticalAlignment.Center;
            var label = new TextBlock { Text = LocalizationService.Get(key) };
            System.Windows.Automation.AutomationProperties.SetLabeledBy(input, label);
            field.Children.Add(label);
            field.Children.Add(input);
            filters.Children.Add(field);
        }
    }

    private void ApplyFilters()
    {
        var selected = _sessions.SelectedItems.OfType<FarmingSession>().Select(row => row.Id).ToHashSet();
        _cards.Children.Clear();
        _filterError.Visibility = Visibility.Collapsed;
        if (_from.SelectedDate > _through.SelectedDate)
        {
            _sessions.ItemsSource = null;
            _filterError.Text = LocalizationService.Get("HistoryInvalidDates");
            _filterError.Visibility = Visibility.Visible;
            return;
        }

        decimal minimumMinutes = 0;
        if (!string.IsNullOrWhiteSpace(_minimumMinutes.Text) &&
            (!decimal.TryParse(_minimumMinutes.Text, out minimumMinutes) || minimumMinutes < 0))
        {
            _sessions.ItemsSource = null;
            _filterError.Text = LocalizationService.Get("HistoryInvalidDuration");
            _filterError.Visibility = Visibility.Visible;
            return;
        }

        var result = FarmingHistoryQuery.Filter(_history, _from.SelectedDate, _through.SelectedDate, _item.Text,
            minimumMinutes, _tag.Text.Trim());
        _sessions.ItemsSource = result.Sessions;
        foreach (var row in result.Sessions.Where(row => selected.Contains(row.Id)))
            _sessions.SelectedItems.Add(row);

        AddCard("HistoryCardSessions", $"{result.Sessions.Length:N0}");
        AddCard("HistoryCardRevenue", $"{result.Revenue:N0} z");
        AddCard("SessionExpenses", $"{result.Expenses:N0} z");
        AddCard("SessionNetValue", $"{result.NetIncome:N0} z");
        AddCard("HistoryCardMedian", result.MedianNetIncome is { } median ? $"{median:N0} z" : "—");
        AddCard("HistoryCardBestRevenue", result.Best?.DateText ?? "—");
        if (!string.IsNullOrWhiteSpace(_item.Text))
            AddCard("HistoryCardItemQuantity", $"{result.ItemQuantity:N0}");
        UpdateCardColumns();
    }

    private void UpdateCardColumns()
    {
        var count = _cards.Children.Count;
        _cards.Columns = Math.Max(1, Math.Min(count, (int)(_cards.ActualWidth / 155)));
    }

    private void AddCard(string captionKey, string value)
    {
        var body = new StackPanel();
        var caption = new TextBlock { Text = LocalizationService.Get(captionKey) };
        caption.SetResourceReference(StyleProperty, "DashboardLabelStyle");
        caption.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(caption);
        body.Children.Add(new TextBlock
        {
            Text = value, FontWeight = FontWeights.SemiBold, FontSize = 14,
            TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 3, 0, 0)
        });
        var card = new Border
        {
            Child = body, Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(3, 0, 3, 4)
        };
        card.SetResourceReference(StyleProperty, "DashboardCardStyle");
        _cards.Children.Add(card);
    }

    internal static void AddColumn(DataGrid grid, string key, string path) =>
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get(key),
            Binding = new Binding(path), Width = new DataGridLength(path is "CharacterCaption" or "Name" ? 1.7 : 1, DataGridLengthUnitType.Star) });
    private void OpenSelected()
    {
        if (_sessions.SelectedItems.Count != 1 || _sessions.SelectedItem is not FarmingSession session) return;
        var owner = Window.GetWindow(this);
        var details = new FarmingSessionDetailsWindow(session, (owner as MainWindow)?.Settings.RogueMode == true)
            { Owner = owner };
        details.ShowDialog();
        RefreshRows();
    }
    private void CompareSelected()
    {
        var sessions = _sessions.SelectedItems.OfType<FarmingSession>().OrderBy(row => row.StartedUtc).ToArray();
        if (sessions.Length != 2) return;

        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true,
            CanUserAddRows = false, CanUserDeleteRows = false, Margin = new Thickness(16) };
        AddColumn(grid, "CharacterLabel", "Name");
        AddColumn(grid, "HistoryDate", "DateText");
        AddColumn(grid, "SessionTime", "DurationText");
        AddColumn(grid, "ZenyTotalLabel", "ZenyGained");
        AddColumn(grid, "AccountedLootValue", "LootValue");
        AddColumn(grid, "SessionExpenses", "ExpensesZeny");
        AddColumn(grid, "SessionNetValue", "NetValue");
        AddColumn(grid, "SessionNetPerHour", "NetPerHour");
        foreach (var column in grid.Columns.OfType<DataGridTextColumn>().Skip(3))
            if (column.Binding is Binding binding)
            {
                binding.StringFormat = "N0";
                binding.TargetNullValue = "—";
            }

        grid.ItemsSource = sessions.Select(row => new
        {
            Name = row.CharacterCaption, row.DateText, row.DurationText, row.ZenyGained,
            row.LootValue, row.ExpensesZeny, row.NetValue, row.NetPerHour
        }).ToArray();
        var attribution = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true,
            CanUserAddRows = false, CanUserDeleteRows = false, Margin = new Thickness(16) };
        AddColumn(attribution, "Item", "Name");
        AddColumn(attribution, "HistoryLootFirst", "First");
        AddColumn(attribution, "HistoryLootSecond", "Second");
        AddColumn(attribution, "FarmPlanDifference", "Difference");
        AddColumn(attribution, "HistoryShareFirst", "FirstShare");
        AddColumn(attribution, "HistoryShareSecond", "SecondShare");
        foreach (var column in attribution.Columns.OfType<DataGridTextColumn>().Skip(1))
            if (column.Binding is Binding binding)
                binding.StringFormat = binding.Path.Path.EndsWith("Share", StringComparison.Ordinal) ? "P1" : "N0";
        var first = sessions[0].Loot.GroupBy(row => row.ItemId).ToDictionary(group => group.Key,
            group => (Name: group.First().Name, Value: group.Sum(row => (decimal)row.Total)));
        var second = sessions[1].Loot.GroupBy(row => row.ItemId).ToDictionary(group => group.Key,
            group => (Name: group.First().Name, Value: group.Sum(row => (decimal)row.Total)));
        var firstTotal = sessions[0].LootValue;
        var secondTotal = sessions[1].LootValue;
        attribution.ItemsSource = first.Keys.Union(second.Keys).Select(id =>
        {
            var before = first.GetValueOrDefault(id);
            var after = second.GetValueOrDefault(id);
            return new { Name = $"{after.Name ?? before.Name} · #{id}", First = before.Value, Second = after.Value,
                Difference = after.Value - before.Value, FirstShare = firstTotal == 0 ? 0 : before.Value / firstTotal,
                SecondShare = secondTotal == 0 ? 0 : after.Value / secondTotal };
        }).OrderByDescending(row => Math.Abs(row.Difference)).ThenBy(row => row.Name).ToArray();
        var comparisons = new TabControl();
        comparisons.SetResourceReference(StyleProperty, "SectionTabControlStyle");
        comparisons.Items.Add(new TabItem { Header = LocalizationService.Get("CompareSessions"), Content = grid });
        comparisons.Items.Add(new TabItem { Header = LocalizationService.Get("HistoryLootContribution"), Content = attribution });
        var window = new Window { Owner = Window.GetWindow(this), Content = comparisons,
            Title = LocalizationService.Get("CompareSessions"), Width = 1000, Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(Control.BackgroundProperty, "RoWindowBrush");
        window.SetResourceReference(Control.ForegroundProperty, "RoTextBrush");
        window.ShowDialog();
    }

    private void RefreshRows()
    {
        try
        {
            if (!((App)Application.Current).FlushFarmingHistory()) return;
            _history = Store.List();
            ApplyFilters();
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), LocalizationService.Get("SessionHistoryError") + "\n" + ex.Message); }
    }
    private void DeleteSelected()
    {
        if (_sessions.SelectedItems.Count != 1 || _sessions.SelectedItem is not FarmingSession session) return;
        if (MessageBox.Show(Window.GetWindow(this), LocalizationService.Get("DeleteSessionConfirm"), LocalizationService.Get("SessionHistory"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { Store.Delete(session.Id); RefreshRows(); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, LocalizationService.Get("SessionHistory"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
