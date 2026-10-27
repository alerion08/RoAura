using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

/// <summary>Shared inline/dialog draft editor. Suggestions never write into the typing draft.</summary>
public partial class MarketWatchEditor : UserControl
{
    private GameDatabaseService? _database;
    private DatabaseItem? _selected;
    private bool _updatingSearch;

    public MarketWatchEditor()
    {
        InitializeComponent();
        Unloaded += (_, _) => SuggestionsPopup.IsOpen = false;
        IsVisibleChanged += (_, _) => { if (!IsVisible) SuggestionsPopup.IsOpen = false; };
    }

    public void Initialize(GameDatabaseService database, DatabaseItem? item = null, MarketWatchRule? rule = null, bool allowItemChange = false)
    {
        _database = database;
        EditorTitle.SetResourceReference(TextBlock.TextProperty, rule is null ? "WatchAddTitle" : "EditItem");
        _updatingSearch = true;
        _selected = item;
        ItemSearch.IsReadOnly = item is not null && !allowItemChange;
        SearchHost.Visibility = ItemSearch.IsReadOnly ? Visibility.Collapsed : Visibility.Visible;
        ItemSearch.Text = item?.Name ?? "";
        ItemSearch.CaretIndex = ItemSearch.Text.Length;
        SelectedItemName.Text = item?.Name ?? "—";
        SelectedItemLabel.Text = item is null ? "" : $"Item ID: {item.Id}";
        PriceBox.Text = rule is null || rule.WithoutPrice ? "" : rule.MaximumPrice.ToString(CultureInfo.InvariantCulture);
        WithoutPrice.IsChecked = rule?.WithoutPrice == true;
        ErrorText.Text = "";
        Suggestions.ItemsSource = null;
        SuggestionsPopup.IsOpen = false;
        _updatingSearch = false;
    }

    public void FocusEditor() => (ItemSearch.IsReadOnly ? PriceBox : ItemSearch).Focus();

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updatingSearch || Suggestions is null || _database is null || ItemSearch.IsReadOnly) return;
        _selected = null;
        SelectedItemName.Text = "—";
        SelectedItemLabel.Text = "";
        ErrorText.Text = "";
        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        if (_database is null) return;
        var query = ItemSearch.Text.Trim();
        Suggestions.ItemsSource = query.Length == 0 ? Array.Empty<DatabaseItem>() :
            _database.Search(DatabaseSearchKind.Items, query, 30).Select(r => (DatabaseItem)r.Value).ToArray();
        Suggestions.SelectedIndex = -1;
        // Opening does not move focus, select a result, replace Text, or touch the caret.
        SuggestionsPopup.IsOpen = IsLoaded && ItemSearch.IsKeyboardFocusWithin && Suggestions.Items.Count > 0;
    }

    private void Search_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (!_updatingSearch && !ItemSearch.IsReadOnly) RefreshSuggestions(); }

    private void Search_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!ItemSearch.IsKeyboardFocusWithin && !Suggestions.IsKeyboardFocusWithin) SuggestionsPopup.IsOpen = false;
    }, DispatcherPriority.Input);

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SuggestionsPopup.IsOpen) { SuggestionsPopup.IsOpen = false; e.Handled = true; return; }
        if (e.Key != Key.Down || Suggestions.Items.Count == 0) return;
        SuggestionsPopup.IsOpen = true;
        Suggestions.SelectedIndex = 0;
        Suggestions.ScrollIntoView(Suggestions.SelectedItem);
        Suggestions.UpdateLayout();
        (Suggestions.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
        e.Handled = true;
    }

    private void Suggestion_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(Suggestions, source) is not ListBoxItem row) return;
        ConfirmSuggestion(row.Content as DatabaseItem);
        e.Handled = true;
    }

    private void Suggestion_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ConfirmSuggestion(Suggestions.SelectedItem as DatabaseItem); e.Handled = true; }
        else if (e.Key == Key.Escape) { ItemSearch.Focus(); SuggestionsPopup.IsOpen = false; e.Handled = true; }
    }

    private void ConfirmSuggestion(DatabaseItem? item)
    {
        if (item is null) return;
        _updatingSearch = true;
        _selected = item;
        ItemSearch.Text = item.Name;
        ItemSearch.CaretIndex = ItemSearch.Text.Length;
        SelectedItemName.Text = item.Name;
        SelectedItemLabel.Text = $"Item ID: {item.Id}";
        SuggestionsPopup.IsOpen = false;
        _updatingSearch = false;
        PriceBox.Focus();
    }

    private void PriceMode_Changed(object sender, RoutedEventArgs e)
    { if (PriceBox is not null) PriceBox.IsEnabled = WithoutPrice.IsChecked != true; }

    public MarketWatchRule? CreateRule()
    {
        if (_database is null) return null;
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
        if (item is null) { ErrorText.Text = LocalizationService.Get("WatchSelectItem"); return null; }
        long price = 0;
        if (WithoutPrice.IsChecked != true &&
            (!long.TryParse(PriceBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out price) || price < 0))
        { ErrorText.Text = LocalizationService.Get("WatchInvalidPrice"); return null; }
        ErrorText.Text = "";
        return new MarketWatchRule { ItemId = item.Id, ItemName = item.Name, WithoutPrice = WithoutPrice.IsChecked == true, MaximumPrice = price };
    }
}
