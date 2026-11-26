using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;
namespace RoAura;

public sealed class FarmingSessionDetailsWindow : Window
{
    public FarmingSessionDetailsWindow(FarmingSession session)
    {
        Title = LocalizationService.Get("SessionLootDetails");
        Width = 850; Height = 560; MinWidth = 620; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"RoWindowBrush");
        SetResourceReference(ForegroundProperty,"RoTextBrush");
        var root=new Grid {Margin=new Thickness(16)};
        root.RowDefinitions.Add(new() {Height=GridLength.Auto});
        root.RowDefinitions.Add(new() {Height=new GridLength(1,GridUnitType.Star)});
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = session.CharacterCaption, FontWeight = FontWeights.Bold });
        var summary = new TextBlock { Margin = new Thickness(0, 6, 0, 6) };
        void RefreshSummary() => summary.Text =
            $"{LocalizationService.Get("SessionNetValue")} · {session.NetValue:N0} z";
        RefreshSummary();
        header.Children.Add(summary);
        var stolen = session.Loot.Sum(row => (decimal)row.StolenRevenue);
        if (session.Loot.Any(row => row.StolenCount > 0))
        {
            var share = session.LootValue > 0
                ? (stolen / session.LootValue).ToString("P1", CultureInfo.CurrentCulture) : "—";
            header.Children.Add(new TextBlock
            {
                Text = string.Format(LocalizationService.Get("StealRevenueShare"), stolen, session.LootValue, share),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6)
            });
        }
        var expenseRow = new WrapPanel();
        var input = new TextBox { Text = session.ExpensesZeny.ToString(CultureInfo.CurrentCulture), MinWidth = 110 };
        expenseRow.Children.Add(new Label { Content = LocalizationService.Get("SessionExpenses"), Target = input });
        expenseRow.Children.Add(input);
        var save = new Button { Content = LocalizationService.Get("Save"), Margin = new Thickness(6, 0, 0, 0) };
        save.Click += (_, _) =>
        {
            if (!long.TryParse(input.Text, NumberStyles.Integer | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture, out var expenses) || expenses < 0)
            {
                MessageBox.Show(this, LocalizationService.Get("SessionExpensesInvalid"));
                return;
            }
            try
            {
                ((App)Application.Current).SetFarmingSessionExpenses(session.Id, expenses);
                session.ExpensesZeny = expenses;
                input.Text = expenses.ToString(CultureInfo.CurrentCulture);
                RefreshSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Get("SessionHistoryError") + "\n" + ex.Message);
            }
        };
        expenseRow.Children.Add(save);
        header.Children.Add(expenseRow);
        var revalue = new Button { Content = LocalizationService.Get("SessionRevalue") };
        var revised = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 4) };
        var changes = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false };
        FarmingSessionHistoryControl.AddColumn(changes, "Item", "Name");
        FarmingSessionHistoryControl.AddColumn(changes, "SessionPriceItemId", "ItemId");
        FarmingSessionHistoryControl.AddColumn(changes, "Count", "Quantity");
        FarmingSessionHistoryControl.AddColumn(changes, "SessionSavedPriceSource", "SavedSource");
        FarmingSessionHistoryControl.AddColumn(changes, "SessionSavedPrice", "SavedPrice");
        FarmingSessionHistoryControl.AddColumn(changes, "SessionCurrentPriceSource", "CurrentSource");
        FarmingSessionHistoryControl.AddColumn(changes, "SessionCurrentPrice", "CurrentPrice");
        foreach (var column in changes.Columns.OfType<DataGridTextColumn>().Where(column =>
                     column.Binding is System.Windows.Data.Binding { Path.Path: "SavedPrice" or "CurrentPrice" }))
        {
            var binding = (System.Windows.Data.Binding)column.Binding;
            binding.TargetNullValue = "—";
            binding.StringFormat = "N0";
        }
        var tabs = new TabControl();
        tabs.SetResourceReference(StyleProperty, "SectionTabControlStyle");
        var valuation = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false };
        FarmingSessionHistoryControl.AddColumn(valuation, "PriceSource", "Source");
        FarmingSessionHistoryControl.AddColumn(valuation, "AccountedLootValue", "Value");
        ((System.Windows.Data.Binding)((DataGridTextColumn)valuation.Columns[1]).Binding).StringFormat = "N0";
        valuation.ItemsSource = session.Loot.GroupBy(row => row.Excluded ? "Excluded" : row.PriceSource)
            .Select(group => new { Source = group.Key, Value = group.Sum(row => (decimal)row.Total) })
            .OrderByDescending(row => row.Value).ToArray();
        var changesTab = new TabItem { Content = changes, Visibility = Visibility.Collapsed };
        revalue.Click += (_, _) =>
        {
            if (((App)Application.Current).RevalueFarmingSession(session) is not { } estimate)
            {
                changes.ItemsSource = null;
                HideChanges();
                revised.Text = LocalizationService.Get("SessionRevalueServer");
                return;
            }
            revised.Text = string.Format(LocalizationService.Get("SessionRevalueResult"),
                session.LootValue, estimate.Total, estimate.Total - session.LootValue, estimate.Unpriced);
            revised.Text += " · " + string.Format(LocalizationService.Get("SessionPriceChanges"), estimate.Changes.Count);
            changes.ItemsSource = estimate.Changes;
            changesTab.Header = string.Format(LocalizationService.Get("SessionPriceChanges"), estimate.Changes.Count);
            if (estimate.Changes.Count > 0) changesTab.Visibility = Visibility.Visible;
            else HideChanges();
        };
        header.Children.Add(revalue);
        header.Children.Add(revised);
        root.Children.Add(header);
        var grid=new DataGrid {AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,ItemsSource=session.Loot};
        FarmingSessionHistoryControl.AddColumn(grid,"Item","Name");
        FarmingSessionHistoryControl.AddColumn(grid,"Count","Count");
        FarmingSessionHistoryControl.AddColumn(grid,"HistoryUnitPrice","UnitPrice");
        FarmingSessionHistoryControl.AddColumn(grid,"PriceSource","PriceSource");
        FarmingSessionHistoryControl.AddColumn(grid,"Total","Total");
        if (session.Loot.Any(row => row.StolenCount > 0))
        {
            FarmingSessionHistoryControl.AddColumn(grid, "LootStolenCount", nameof(FarmingLootEntry.StolenCount));
            FarmingSessionHistoryControl.AddColumn(grid, "LootStolenRevenue", nameof(FarmingLootEntry.StolenRevenue));
            FarmingSessionHistoryControl.AddColumn(grid, "LootRegularCount", nameof(FarmingLootEntry.RegularCount));
            FarmingSessionHistoryControl.AddColumn(grid, "LootRegularRevenue", nameof(FarmingLootEntry.RegularRevenue));
        }
        var lootPanel = new DockPanel();
        var hideZero = new CheckBox { Content = LocalizationService.Get("HideZeroValueLoot"), Margin = new Thickness(4, 5, 0, 8) };
        var questOnly = new CheckBox { Content = LocalizationService.Get("QNeededForQuest"), Margin = new Thickness(4, 5, 0, 8),
            Visibility = GameServerProfile.Normalize(session.Server) == GameServerProfile.RagnarokPlus ? Visibility.Visible : Visibility.Collapsed };
        void FilterLoot(object sender, RoutedEventArgs e) => grid.ItemsSource = session.Loot.Where(row =>
            (hideZero.IsChecked != true || row.Total != 0) &&
            (questOnly.IsChecked != true || RagnarokPlusQuestCatalog.Instance.RequiredFor(session.Server, row.ItemId).Any())).ToArray();
        hideZero.Click += FilterLoot;
        questOnly.Click += FilterLoot;
        var filters = new WrapPanel();
        filters.Children.Add(hideZero);
        filters.Children.Add(questOnly);
        DockPanel.SetDock(filters, Dock.Top);
        lootPanel.Children.Add(filters);
        lootPanel.Children.Add(grid);
        Loaded += (_, _) =>
        {
            if (Owner is MainWindow owner)
                TableLayout.Attach(grid, owner.Settings,
                    grid.Columns.Count > 5 ? "gold-goblin-history-steal" : "gold-goblin-history-loot", owner.RequestSave);
        };
        tabs.Items.Add(new TabItem { Header = LocalizationService.Get("SessionLootDetails"), Content = lootPanel });
        tabs.Items.Add(changesTab);
        tabs.Items.Add(new TabItem { Header = LocalizationService.Get("HistoryPriceBreakdown"), Content = valuation });
        tabs.Items.Add(new TabItem { Header = LocalizationService.Get("SessionTimeline"),
            Content = new FarmingSessionTimeline(session) });
        if (session.Plan is { } plan)
            tabs.Items.Add(new TabItem { Header = LocalizationService.Get("FarmPlanComparison"),
                Content = BuildPlanComparison(session, plan) });
        tabs.Items.Add(new TabItem { Header = LocalizationService.Get("SessionNote"), Content = BuildNoteEditor(session) });
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        Content = root;

        void HideChanges()
        {
            if (tabs.SelectedItem == changesTab) tabs.SelectedIndex = 0;
            changesTab.Visibility = Visibility.Collapsed;
        }
    }

    private FrameworkElement BuildNoteEditor(FarmingSession session)
    {
        var form = new StackPanel { Margin = new Thickness(12) };
        var tags = new TextBox { Text = session.Tags, MaxLength = 200 };
        form.Children.Add(new Label { Content = LocalizationService.Get("HistorySessionTags"), Target = tags });
        form.Children.Add(tags);
        var note = new TextBox
        {
            Text = session.Note, MaxLength = 500, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 80, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var save = new Button { Content = LocalizationService.Get("Save"), HorizontalAlignment = HorizontalAlignment.Left };
        var status = new TextBlock();
        note.TextChanged += (_, _) => status.Text = "";
        tags.TextChanged += (_, _) => status.Text = "";
        save.Click += (_, _) =>
        {
            var value = note.Text.Trim();
            try
            {
                var app = (App)Application.Current;
                var cleanTags = tags.Text.Trim();
                app.SetFarmingSessionNote(session.Id, value, cleanTags);
                session.Tags = cleanTags;
                tags.Text = cleanTags;
                session.Note = value;
                note.Text = value;
                status.Text = LocalizationService.Get("SessionNoteSaved");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Get("SessionHistoryError") + "\n" + ex.Message);
            }
        };
        form.Children.Add(note);
        form.Children.Add(save);
        form.Children.Add(status);
        return form;
    }

    private static FrameworkElement BuildPlanComparison(FarmingSession session, FarmingPlan plan)
    {
        var contents = new DockPanel { Margin = new Thickness(8) };
        var header = new TextBlock
        {
            Text = $"{plan.MapName} ({plan.MapCode}) · {LocalizationService.Get("FarmPlanSource" + plan.PriceSource)}\n" +
                string.Join(" · ", plan.Targets.Select(target => $"{target.Name} ×{target.Kills:N0}")) +
                "\n" + LocalizationService.Get("FarmPlanActualScope"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(header, Dock.Top);
        contents.Children.Add(header);
        var planned = plan.Drops.ToDictionary(row => row.ItemId);
        var actual = session.Loot.GroupBy(row => row.ItemId).ToDictionary(group => group.Key,
            group => (Name: group.First().Name, Quantity: group.Sum(row => (decimal)row.Count)));
        var rows = planned.Keys.Union(actual.Keys).Select(id =>
        {
            var expected = planned.GetValueOrDefault(id);
            var received = actual.GetValueOrDefault(id);
            var quantity = expected?.Quantity ?? 0;
            return new { Name = expected?.Name ?? received.Name, Planned = quantity,
                Actual = received.Quantity, Difference = received.Quantity - quantity };
        }).OrderBy(row => row.Name).ToArray();
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, ItemsSource = rows };
        FarmingSessionHistoryControl.AddColumn(grid, "Item", "Name");
        FarmingSessionHistoryControl.AddColumn(grid, "FarmPlanPlanned", "Planned");
        FarmingSessionHistoryControl.AddColumn(grid, "FarmPlanActual", "Actual");
        FarmingSessionHistoryControl.AddColumn(grid, "FarmPlanDifference", "Difference");
        contents.Children.Add(grid);
        return contents;
    }
}
