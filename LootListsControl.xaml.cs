using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class LootListsControl : UserControl
{
    public sealed record Row(LootList List, LootListItem? Item, IReadOnlyList<LootList> Lists, string Name)
    {
        public bool ShowInTracker => Item is not null && LootLists.IsShownInTracker(List, Item);

        public Visibility ItemVisibility => Item is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility EmptyListVisibility => Item is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private AppSettings? _settings;
    private Action? _save;
    private Func<int, string?>? _resolveName;
    private bool _updating;
    private Action? _restoreLayout;
    private LootListItem[] _observed = [];

    public LootListsControl()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshLists();
        Unloaded += (_, _) => Observe([]);
    }

    internal void Initialize(AppSettings settings, Action save, Func<int, string?> resolveName)
    {
        _settings = settings;
        _save = save;
        _resolveName = resolveName;
        TrackerOptions.DataContext = settings.LootListTracker;
        TableLayout.Attach(ItemsGrid, settings, "gold-goblin-loot-lists", save);
        _restoreLayout = GoldGoblinSplitLayout.Attach(EditorSplitter, EditorColumn,
            () => settings.GoldGoblinLootListEditorRatio, value => settings.GoldGoblinLootListEditorRatio = value, save);
        RefreshLists();
    }

    internal void RestoreLayout() => _restoreLayout?.Invoke();

    internal void RefreshLists()
    {
        if (_settings is null || _updating) return;
        _updating = true;
        try
        {
            var lists = LootLists.ForServer(_settings, _settings.ServerProfile).ToArray();
            var selected = (ListFilter.SelectedItem as ComboBoxItem)?.Tag as LootList;
            ListFilter.ItemsSource = lists.Select(list => new ComboBoxItem { Content = list.Name, Tag = list })
                .Prepend(new ComboBoxItem { Content = LocalizationService.Get("LootListAll") }).ToArray();
            ListFilter.SelectedItem = ListFilter.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag == selected)
                ?? ListFilter.Items[0];
            RenderRows(lists);
        }
        finally { _updating = false; }
    }

    private LootList? SelectedList => (ListFilter.SelectedItem as ComboBoxItem)?.Tag as LootList;

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || SelectedList is not { } list || Window.GetWindow(this) is not { } owner) return;
        if (LootListNameDialog.Create(owner, _settings, list.Server, list) is null) return;
        _save?.Invoke();
        RefreshLists();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || SelectedList is not { } list || Window.GetWindow(this) is not { } owner) return;
        var prompt = string.Format(LocalizationService.Get("LootListDeleteConfirm"), list.Name);
        if (!LootListNameDialog.Confirm(owner, prompt, LocalizationService.Get("Delete"))) return;
        _settings.LootLists.Remove(list);
        _save?.Invoke();
        RefreshLists();
    }

    private void RenderRows(LootList[] lists)
    {
        var selected = SelectedList;
        RenameListButton.IsEnabled = DeleteListButton.IsEnabled = selected is not null;
        var visibleLists = lists.Where(list => selected is null || list == selected).ToArray();
        ItemsGrid.ItemsSource = visibleLists.SelectMany(list => list.Items.Count == 0
            ? Enumerable.Repeat(new Row(list, null, [], ""), 1)
            : list.Items.Select(item => new Row(list, item,
                lists.Where(candidate => candidate == list || candidate.Items.All(row => row.ItemId != item.ItemId)).ToArray(),
                item.Name == $"Item #{item.ItemId}" ? _resolveName?.Invoke(item.ItemId) ?? item.Name : item.Name))).ToArray();
        Observe(visibleLists.SelectMany(list => list.Items).ToArray());
    }

    private void Observe(LootListItem[] items)
    {
        foreach (var item in _observed) item.PropertyChanged -= ItemChanged;
        _observed = items;
        foreach (var item in _observed) item.PropertyChanged += ItemChanged;
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LootListItem.Target) or nameof(LootListItem.Active)) _save?.Invoke();
    }

    private void ListFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && _settings is not null) RenderRows(LootLists.ForServer(_settings, _settings.ServerProfile).ToArray());
    }

    private void ItemList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || sender is not ComboBox { DataContext: Row { Item: { } item } row, SelectedItem: LootList destination } box ||
            ReferenceEquals(row.List, destination) || !box.IsDropDownOpen && !box.IsKeyboardFocusWithin) return;
        if (LootLists.MoveItem(row.List, destination, item))
        {
            _save?.Invoke();
            Dispatcher.BeginInvoke(RefreshLists);
        }
        else {
            _updating = true;
            try { box.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, row.List); }
            finally { _updating = false; }
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || Window.GetWindow(this) is not { } owner) return;
        if (LootListNameDialog.Create(owner, _settings, _settings.ServerProfile) is null) return;
        _save?.Invoke();
        RefreshLists();
    }

    private void ItemActive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: Row { Item: { } item } } box)
            item.Active = box.IsChecked == true;
    }

    private void ItemTracker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: Row { Item: { } item } } box) return;
        item.ShowInTracker = box.IsChecked == true;
        _save?.Invoke();
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Row row }) RemoveItem(row);
    }

    private void ItemsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase ||
            ItemsGrid.SelectedItem is not Row row) return;
        e.Handled = RemoveItem(row);
    }

    private bool RemoveItem(Row row)
    {
        if (row.Item is null || !row.List.Items.Remove(row.Item)) return false;
        _save?.Invoke();
        RefreshLists();
        return true;
    }

    private void TrackerOptions_Changed(object sender, DataTransferEventArgs e) => _save?.Invoke();
}
