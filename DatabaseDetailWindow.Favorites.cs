using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Models;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    private List<DatabaseFavoriteList> FavoriteLists =>
        _settings.DatabaseFavoriteLists.GetValueOrDefault(_database.Version) ?? [];

    private void AddFavoriteAction(DatabaseFavorite entry)
    {
        AddLink(T("Добавить в список…", "Add to favorites list…"), () => ShowFavoriteLists(entry));
    }

    private void AddItemActions(DatabaseItem item)
    {
        var entry = new DatabaseFavorite(DatabaseSearchKind.Items, item.Id.ToString(), item.Name);
        var actions = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        if (GameServerProfile.SupportsMarketWatch(_database.Version))
        {
            actions.Children.Add(NewWatch("SELL", "Sell"));
            if (_database.Version == GameServerProfile.RagnarokPlus) actions.Children.Add(NewWatch("BUY", "Buy"));
        }
        var favorite = new Button { Content = ItemActionLabel("Favorite"), FontSize = 12, MinHeight = 32, Margin = new Thickness(2), Padding = new Thickness(3) };
        if (FavoriteLists.Any(list => list.Entries.Any(row => row.Kind == entry.Kind && row.Key == entry.Key)))
            favorite.SetResourceReference(StyleProperty, "ActiveItemCardButtonStyle");
        favorite.Click += (_, _) => ShowFavoriteLists(entry);
        actions.Children.Add(favorite);
        DetailsPanel.Children.Add(actions);

        Controls.MarketWatchStar NewWatch(string side, string label) => new()
        {
            UseLabeledStyle = true,
            Item = new MarketListing { ItemId = item.Id, ItemName = item.Name, ShopType = side },
            Content = ItemActionLabel(label), Width = double.NaN, Height = double.NaN, FontSize = 12,
            MinHeight = 32, Margin = new Thickness(2), Padding = new Thickness(3)
        };
    }

    private static TextBlock ItemActionLabel(string caption)
    {
        var label = new TextBlock { Text = caption + " ★" };
        label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Button.Foreground))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(
                System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
        });
        return label;
    }

    internal void ShowFavoriteLists(DatabaseFavorite? entry = null, string? selectedName = null, bool navigate = true)
    {
        if (navigate) Navigate(() => ShowFavoriteLists(entry, selectedName, false));
        ++_detailVersion;
        Clear();
        _appearanceRedraw = () => ShowFavoriteLists(entry, selectedName, false);
        RefreshBackButton();
        DetailsScroll.ScrollToTop();
        AddSection(T("Избранное", "Favorites"));
        if (entry is not null) DetailsPanel.Children.Add(Meta(entry.Name));

        var lists = FavoriteLists;
        var chooser = new ComboBox { ItemsSource = lists.Select(list => list.Name).ToArray(), Margin = new Thickness(0, 4, 0, 4) };
        DetailsPanel.Children.Add(chooser);
        var name = new TextBox { MaxLength = 80, Margin = new Thickness(0, 3, 0, 2) };
        DetailsPanel.Children.Add(Meta(T("Название списка", "List name")));
        DetailsPanel.Children.Add(name);
        var error = Meta("");
        AddLink(T("Создать список", "Create list"), () =>
        {
            var title = name.Text.Trim();
            if (title.Length == 0 || lists.Any(list => string.Equals(list.Name, title, StringComparison.OrdinalIgnoreCase)))
            {
                error.Text = T("Укажите уникальное название списка.", "Enter a unique list name.");
                return;
            }
            lists.Add(new DatabaseFavoriteList { Name = title });
            _settings.DatabaseFavoriteLists[_database.Version] = lists;
            SaveFavorites();
            ShowFavoriteLists(entry, title, false);
        });
        var management = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        var rename = new Button { Content = T("Переименовать", "Rename") };
        var delete = new Button { Content = T("Удалить список", "Delete list") };
        management.Children.Add(rename);
        management.Children.Add(delete);
        DetailsPanel.Children.Add(management);
        rename.Click += (_, _) =>
        {
            if (chooser.SelectedIndex < 0) return;
            var list = lists[chooser.SelectedIndex];
            var title = name.Text.Trim();
            if (title.Length == 0 || lists.Any(other => other != list &&
                string.Equals(other.Name, title, StringComparison.OrdinalIgnoreCase)))
            {
                error.Text = T("Укажите уникальное название списка.", "Enter a unique list name.");
                return;
            }
            list.Name = title;
            SaveFavorites();
            ShowFavoriteLists(entry, title, false);
        };
        delete.Click += (_, _) =>
        {
            if (chooser.SelectedIndex < 0) return;
            var list = lists[chooser.SelectedIndex];
            if (MessageBox.Show(this, T($"Удалить список «{list.Name}» со всеми закладками?",
                $"Delete “{list.Name}” and its bookmarks?"), T("Избранное", "Favorites"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            lists.Remove(list);
            SaveFavorites();
            ShowFavoriteLists(entry, null, false);
        };
        DetailsPanel.Children.Add(error);
        var contents = new StackPanel();
        DetailsPanel.Children.Add(contents);

        void RenderList()
        {
            contents.Children.Clear();
            rename.IsEnabled = delete.IsEnabled = chooser.SelectedIndex >= 0;
            if (chooser.SelectedIndex < 0) return;
            var list = lists[chooser.SelectedIndex];
            selectedName = list.Name;
            _currentPage = () => ShowFavoriteLists(entry, selectedName, false);
            var summary = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
            var color = new Button { Padding = new Thickness(5, 2, 5, 2), Content = T("Цвет", "Color") };
            DockPanel.SetDock(color, Dock.Right);
            color.Click += (_, _) =>
            {
                var picker = new ColorPickerWindow(string.IsNullOrWhiteSpace(list.ColorHex) ? "#FF2863AA" : list.ColorHex)
                {
                    Owner = this, Topmost = Topmost
                };
                if (picker.ShowDialog() != true) return;
                list.ColorHex = picker.SelectedHex;
                SaveFavorites();
                RenderList();
            };
            summary.Children.Add(color);
            var marker = new Border { Width = 4, CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 3, 6, 3) };
            marker.SetResourceReference(Border.BackgroundProperty, "RoBorderBrush");
            if (!string.IsNullOrWhiteSpace(list.ColorHex))
            {
                try { marker.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(list.ColorHex)); }
                catch (FormatException) { }
            }
            DockPanel.SetDock(marker, Dock.Left);
            summary.Children.Add(marker);
            var items = list.Entries.Count(favorite => favorite.Kind == DatabaseSearchKind.Items);
            var monsters = list.Entries.Count(favorite => favorite.Kind == DatabaseSearchKind.Monsters);
            var maps = list.Entries.Count(favorite => favorite.Kind == DatabaseSearchKind.Maps);
            summary.Children.Add(Meta(T($"Предметы {items} · Монстры {monsters} · Карты {maps}",
                $"Items {items} · Monsters {monsters} · Maps {maps}")));
            contents.Children.Add(summary);

            if (entry is not null)
            {
                var existing = list.Entries.FirstOrDefault(favorite => favorite.Kind == entry.Kind &&
                    string.Equals(favorite.Key, entry.Key, StringComparison.OrdinalIgnoreCase));
                AddFavoriteButton(contents, existing is null ? T("Добавить в этот список", "Add to this list") :
                    T("Убрать из этого списка", "Remove from this list"), () =>
                {
                    if (existing is null) list.Entries.Add(entry);
                    else list.Entries.Remove(existing);
                    SaveFavorites();
                    RenderList();
                });
            }

            if (list.Entries.Count == 0) contents.Children.Add(Meta(T("Список пуст.", "The list is empty.")));
            foreach (var favorite in list.Entries.OrderBy(favorite => favorite.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var remove = new Button { Content = "×", ToolTip = T("Убрать из списка", "Remove from list"),
                    Padding = new Thickness(6, 0, 6, 0) };
                DockPanel.SetDock(remove, Dock.Right);
                remove.Click += (_, _) => { list.Entries.Remove(favorite); SaveFavorites(); RenderList(); };
                row.Children.Add(remove);
                var type = favorite.Kind switch
                {
                    DatabaseSearchKind.Items => T("Предмет", "Item"),
                    DatabaseSearchKind.Monsters => T("Монстр", "Monster"),
                    _ => T("Карта", "Map")
                };
                AddFavoriteButton(row, $"{favorite.Name} · {type}", () => OpenFavorite(favorite),
                    ResolveFavorite(favorite) is not null);
                contents.Children.Add(row);
                AddFavoriteNote(contents, favorite);
            }
        }

        chooser.SelectionChanged += (_, _) =>
        {
            name.Text = chooser.SelectedItem as string ?? "";
            error.Text = "";
            RenderList();
        };
        chooser.SelectedItem = lists.FirstOrDefault(list => list.Name == selectedName)?.Name ?? lists.FirstOrDefault()?.Name;
        RenderList();
    }

    private void AddFavoriteNote(Panel parent, DatabaseFavorite favorite)
    {
        var note = string.IsNullOrWhiteSpace(favorite.Note) ? "" : favorite.Note;
        var input = new TextBox
        {
            Text = note, MaxLength = 500, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 48, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var save = new Button { Content = T("Сохранить", "Save"), HorizontalAlignment = HorizontalAlignment.Left };
        var editor = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
        editor.Children.Add(input);
        editor.Children.Add(save);
        var expander = new Expander { Header = NoteCaption(note), Content = editor, Margin = new Thickness(4, 0, 4, 6) };
        save.Click += (_, _) =>
        {
            favorite.Note = input.Text.Trim();
            input.Text = favorite.Note;
            SaveFavorites();
            expander.Header = NoteCaption(favorite.Note);
            expander.IsExpanded = false;
        };
        parent.Children.Add(expander);

        TextBlock NoteCaption(string text) => Meta(text.Length == 0 ? T("Добавить заметку", "Add note") : text);
    }

    private static void AddFavoriteButton(Panel panel, string label, Action action, bool enabled = true)
    {
        var button = new Button { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
            HorizontalContentAlignment = HorizontalAlignment.Left, IsEnabled = enabled,
            Padding = new Thickness(6, 3, 6, 3) };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private object? ResolveFavorite(DatabaseFavorite entry) => entry.Kind switch
    {
        DatabaseSearchKind.Items when int.TryParse(entry.Key, out var itemId) => _database.Item(itemId),
        DatabaseSearchKind.Monsters when int.TryParse(entry.Key, out var monsterId) => _database.Monster(monsterId),
        DatabaseSearchKind.Maps => _database.Map(entry.Key),
        _ => null
    };

    private void OpenFavorite(DatabaseFavorite entry)
    {
        if (ResolveFavorite(entry) is { } value) ShowDetails(value);
    }

    private static void SaveFavorites() => (Application.Current as App)?.SaveDatabasePreferences();
}
