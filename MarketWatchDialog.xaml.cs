using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MarketWatchDialog : Window
{
    private readonly GameDatabaseService _database;
    private DatabaseItem? _selected;
    public MarketWatchRule? Rule { get; private set; }
    public MarketWatchDialog(GameDatabaseService database, DatabaseItem? item = null, MarketWatchRule? rule = null)
    {
        _database = database;
        InitializeComponent();
        if (item is not null)
        {
            _selected = item;
            ItemSearch.IsReadOnly = true;
            ItemSearch.Text = item.Name;
            Suggestions.Visibility = Visibility.Collapsed;
            SelectedItemLabel.Text = item.Caption;
        }
        if (rule is not null)
        {
            WithoutPrice.IsChecked = rule.WithoutPrice;
            PriceBox.Text = rule.MaximumPrice.ToString(CultureInfo.InvariantCulture);
        }
        Loaded += (_, _) => (item is null ? ItemSearch : PriceBox).Focus();
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (Suggestions is null || ItemSearch.IsReadOnly) return;
        _selected = null;
        if (SelectedItemLabel is not null) SelectedItemLabel.Text = "";
        Suggestions.Visibility = ItemSearch.Text.Trim().Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Suggestions.ItemsSource = ItemSearch.Text.Trim().Length == 0 ? Array.Empty<DatabaseItem>() :
            _database.Search(DatabaseSearchKind.Items, ItemSearch.Text, 30).Select(r => (DatabaseItem)r.Value).ToArray();
        Suggestions.SelectedIndex = -1;
        // Never select a result or rewrite Text while typing. Only a click/arrow+Enter confirms it.
    }
    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || Suggestions.Items.Count == 0) return;
        Suggestions.SelectedIndex = 0;
        Suggestions.Focus();
        e.Handled = true;
    }
    private void Suggestion_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (Suggestions.SelectedItem is not DatabaseItem item) return;
        _selected = item;
        SelectedItemLabel.Text = item.Caption;
    }
    private void PriceMode_Changed(object sender, RoutedEventArgs e)
    { if (PriceBox is not null) PriceBox.IsEnabled = WithoutPrice.IsChecked != true; }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // An exact numeric ID or unique exact name can also be submitted without clicking the suggestion.
        var item = _selected;
        if (item is null)
        {
            var query = ItemSearch.Text.Trim();
            if (int.TryParse(query, out var id)) item = _database.Item(id);
            else
            {
                var exact = _database.Items.Where(i => i.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase)).Take(2).ToList();
                if (exact.Count == 1) item = exact[0];
            }
        }
        if (item is null) { ErrorText.Text = LocalizationService.Get("WatchSelectItem"); return; }
        long price = 0;
        if (WithoutPrice.IsChecked != true &&
            (!long.TryParse(PriceBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out price) || price < 0))
        { ErrorText.Text = LocalizationService.Get("WatchInvalidPrice"); return; }
        Rule = new MarketWatchRule { ItemId = item.Id, ItemName = item.Name, WithoutPrice = WithoutPrice.IsChecked == true, MaximumPrice = price };
        DialogResult = true;
    }
}
