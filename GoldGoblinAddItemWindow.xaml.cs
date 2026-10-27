using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoAura.Models;

namespace RoAura;

public partial class GoldGoblinAddItemWindow : Window
{
    private readonly IReadOnlyList<ItemEntry> _items;
    private readonly ObservableCollection<ItemEntry> _results = [];

    public ItemEntry? SelectedItem { get; private set; }

    public GoldGoblinAddItemWindow(IEnumerable<ItemEntry> items)
    {
        _items = items.OrderBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();
        InitializeComponent();
        ResultsList.ItemsSource = _results;
        Loaded += (_, _) =>
        {
            RefreshResults("");
            SearchBox.Focus();
        };
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshResults(SearchBox.Text);
    }

    private void RefreshResults(string query)
    {
        var normalized = query.Trim();
        var matches = _items.Where(item => normalized.Length == 0 ||
                item.FullName.Contains(normalized, StringComparison.CurrentCultureIgnoreCase) ||
                item.Name.Contains(normalized, StringComparison.CurrentCultureIgnoreCase) ||
                item.Id.ToString().Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(150)
            .ToList();

        _results.Clear();
        foreach (var item in matches) _results.Add(item);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not ItemEntry item)
        {
            MessageBox.Show(this,
                TryFindResource("SelectItemFromList")?.ToString() ?? "Select an item from the list.",
                TryFindResource("AddItem")?.ToString() ?? "Add item",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedItem = item;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Add_Click(sender, e);
}
