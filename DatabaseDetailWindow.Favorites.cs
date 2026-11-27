using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Models;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    private List<DatabaseFavoriteList> FavoriteLists =>
        _settings.DatabaseFavoriteLists.GetValueOrDefault(_database.Version) ?? [];

    private DatabaseFavorite? _favoriteEntry;

    private void SetFavoriteEntry(DatabaseFavorite entry)
    {
        if (_favoriteEntry != entry) FavoriteMenu.Visibility = Visibility.Collapsed;
        _favoriteEntry = entry;
        FavoriteButton.Visibility = Visibility.Visible;
        UpdateFavoriteButton(entry);
    }

    private void UpdateFavoriteButton(DatabaseFavorite entry)
    {
        var active = FavoriteLists.Any(list => list.Entries.Any(row => MatchesFavorite(row, entry)));
        FavoriteButton.Content = active ? "★" : "☆";
        FavoriteButton.SetResourceReference(ForegroundProperty, active ? "RoBlueBrush" : "RoMutedBrush");
    }

    private static bool MatchesFavorite(DatabaseFavorite row, DatabaseFavorite entry) =>
        row.Kind == entry.Kind && string.Equals(row.Key, entry.Key, StringComparison.OrdinalIgnoreCase);

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_favoriteEntry is not { } entry) return;
        if (FavoriteMenu.Visibility == Visibility.Visible)
        {
            FavoriteMenu.Visibility = Visibility.Collapsed;
            return;
        }

        FavoritePopupLists.Children.Clear();
        foreach (var list in FavoriteLists)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(FavoriteColorMarker(list));
            label.Children.Add(new TextBlock { Text = list.Name, MaxWidth = 175, TextWrapping = TextWrapping.Wrap });
            var option = new CheckBox
            {
                Content = label, IsChecked = list.Entries.Any(row => MatchesFavorite(row, entry)),
                Padding = new Thickness(4), Margin = new Thickness(4)
            };
            option.Click += (_, _) =>
            {
                if (option.IsChecked == true)
                {
                    if (!list.Entries.Any(row => MatchesFavorite(row, entry))) list.Entries.Add(entry with { });
                }
                else list.Entries.RemoveAll(row => MatchesFavorite(row, entry));
                SaveFavorites();
                UpdateFavoriteButton(entry);
            };
            FavoritePopupLists.Children.Add(option);
        }

        FavoriteMenu.Visibility = Visibility.Visible;
        if (FavoritePopupLists.Children.Count == 0) CreateFavoriteListButton.Focus();
    }

    private void Details_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!FavoriteButton.IsMouseOver && !FavoriteMenu.IsMouseOver)
            FavoriteMenu.Visibility = Visibility.Collapsed;
    }

    private void Details_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape || FavoriteMenu.Visibility != Visibility.Visible) return;
        FavoriteMenu.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void DetailsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DetailsScroll) && e.VerticalChange != 0 && FavoriteMenu is not null)
            FavoriteMenu.Visibility = Visibility.Collapsed;
    }

    private void CreateFavoriteList_Click(object sender, RoutedEventArgs e)
    {
        if (_favoriteEntry is not { } entry) return;
        FavoriteMenu.Visibility = Visibility.Collapsed;
        var lists = FavoriteLists;
        var list = new DatabaseFavoriteList();
        var name = new TextBox { MaxLength = 80, Margin = new Thickness(0, 4, 0, 8) };
        var color = new Button { Content = T("Цвет списка", "List color"), HorizontalAlignment = HorizontalAlignment.Left };
        var error = Meta("");
        var fields = new StackPanel();
        fields.Children.Add(Meta(T("Название списка", "List name")));
        fields.Children.Add(name);
        fields.Children.Add(color);
        fields.Children.Add(error);
        var dialog = FavoriteDialog(T("Новый список", "New list"), fields, T("Создать", "Create"), () =>
        {
            list.Name = name.Text.Trim();
            if (list.Name.Length > 0 && !lists.Any(other => string.Equals(other.Name, list.Name, StringComparison.OrdinalIgnoreCase)))
                return true;
            error.Text = T("Укажите уникальное название списка.", "Enter a unique list name.");
            name.Focus();
            return false;
        });
        color.Click += (_, _) =>
        {
            if (!PickFavoriteColor(list, dialog)) return;
            var preview = new StackPanel { Orientation = Orientation.Horizontal };
            preview.Children.Add(FavoriteColorMarker(list));
            preview.Children.Add(new TextBlock { Text = T("Цвет списка", "List color") });
            color.Content = preview;
        };
        dialog.Loaded += (_, _) => name.Focus();
        if (dialog.ShowDialog() != true) return;
        list.Entries.Add(entry with { });
        lists.Add(list);
        _settings.DatabaseFavoriteLists[_database.Version] = lists;
        SaveFavorites();
        if (_favoriteEntry == entry) UpdateFavoriteButton(entry);
    }

    private Window FavoriteDialog(string title, FrameworkElement body, string action, Func<bool> accept, bool danger = false)
    {
        var dialog = new Window
        {
            Owner = this, Title = title, Width = 360, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, ShowInTaskbar = false, AllowsTransparency = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = true
        };
        dialog.Background = Brushes.Transparent;
        dialog.SetResourceReference(ForegroundProperty, "RoTextBrush");
        var layout = new DockPanel();
        var header = new Border { Padding = new Thickness(14,9,14,9), CornerRadius = new CornerRadius(8,8,0,0), Child = Heading(title, 17) };
        header.SetResourceReference(Border.BackgroundProperty, "RoTitleBrush");
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12,0,12,10) };
        var cancel = new Button { Content = T("Отмена", "Cancel"), IsCancel = true };
        var confirm = new Button { Content = action, IsDefault = !danger };
        confirm.SetResourceReference(StyleProperty, danger ? "DangerButtonStyle" : "PrimaryButtonStyle");
        confirm.Click += (_, _) => { if (accept()) dialog.DialogResult = true; };
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);
        layout.Children.Add(new Border { Padding = new Thickness(14,8,14,8), Child = body });
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Child = layout };
        frame.SetResourceReference(Border.BackgroundProperty, "RoWindowBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "RoBorderBrush");
        dialog.Content = frame;
        return dialog;
    }

    private static Border FavoriteColorMarker(DatabaseFavoriteList list)
    {
        var marker = new Border { Width = 5, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 1, 7, 1) };
        marker.SetResourceReference(Border.BackgroundProperty, "RoBorderBrush");
        if (ColorPickerWindow.TryParseHex(list.ColorHex, out var color)) marker.Background = new SolidColorBrush(color);
        return marker;
    }

    private System.Windows.Controls.Primitives.Popup? _itemActionsMenu;

    private void AddItemActions(DatabaseItem item, Panel actions)
    {
        SetFavoriteEntry(new(DatabaseSearchKind.Items, item.Id.ToString(), item.Name));
        var server = _database.Version;
        if (!GameServerProfile.SupportsMarketWatch(server) && server != GameServerProfile.RagnarokPlus) return;

        var more = new Button { Content = "⋯", ToolTip = T("Действия предмета", "Item actions") };
        System.Windows.Automation.AutomationProperties.SetName(more, more.ToolTip.ToString());
        var menu = new StackPanel { MinWidth = 190 };
        var frame = new Border { Child = menu, Padding = new Thickness(5), CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "DatabaseField");
        frame.SetResourceReference(Border.BorderBrushProperty, "DatabaseLine");
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true, Child = frame
        };
        _itemActionsMenu = popup;
        var status = new StackPanel();
        if (GameServerProfile.SupportsMarketWatch(server))
        {
            AddWatch("SELL", T("Следить за продажей", "Watch selling"));
            if (server == GameServerProfile.RagnarokPlus) AddWatch("BUY", T("Следить за скупкой", "Watch buying"));
        }
        if (server == GameServerProfile.RagnarokPlus)
            AddLink(T("Карточка и рынок Ragnarok+", "Ragnarok+ item and market"), () =>
            {
                popup.IsOpen = false;
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        $"https://ragnarok.plus/item-db/{item.Id}") { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception ex) { DebugLog.Write($"RAGNAROK+ ITEM LINK ERROR: {ex.Message}"); }
            }, menu);
        more.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        popup.Opened += (_, _) => menu.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        frame.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            popup.IsOpen = false;
            more.Focus();
            e.Handled = true;
        };
        if (actions is System.Windows.Controls.Primitives.UniformGrid equal)
        {
            var parent = new DockPanel { Margin = equal.Margin };
            equal.Margin = new Thickness(0);
            var index = DetailsPanel.Children.IndexOf(equal);
            DetailsPanel.Children.Remove(equal);
            DockPanel.SetDock(more, Dock.Right);
            parent.Children.Add(more);
            parent.Children.Add(equal);
            DetailsPanel.Children.Insert(index, parent);
            DetailsPanel.Children.Insert(index + 1, status);
        }

        void AddWatch(string side, string label)
        {
            var listing = new MarketListing { ItemId = item.Id, ItemName = item.Name, ShopType = side };
            var button = new Button();
            AddLink(label, () =>
            {
                popup.IsOpen = false;
                (Application.Current as App)?.ToggleMarketWatch(this, listing);
                RefreshStatus();
            }, menu);
            button.SetResourceReference(StyleProperty, "DatabaseLinkRow");
            button.Click += (_, _) =>
            {
                var rule = _settings.ActiveMarketWatchRules.FirstOrDefault(rule => rule.ItemId == item.Id && rule.Buying == (side == "BUY"));
                if (rule is not null) (Application.Current as App)?.EditMarketWatch(this, rule);
                RefreshStatus();
            };
            status.Children.Add(button);
            RefreshStatus();

            void RefreshStatus()
            {
                var current = _settings.ActiveMarketWatchRules.FirstOrDefault(rule => rule.ItemId == item.Id && rule.Buying == (side == "BUY"));
                button.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
                button.Content = $"{side} · {(current?.Enabled == true ? T("активно", "active") : T("пауза", "paused"))}";
            }
        }
    }

    internal void ShowFavoriteLists(string? selectedName = null, bool navigate = true)
    {
        if (navigate) Navigate(() => ShowFavoriteLists(selectedName, false));
        ++_detailVersion;
        Clear();
        RefreshBackButton();
        DetailsScroll.ScrollToTop();
        PanelTitle.Text = T("Избранное", "Favorites");
        AddSection(T("Избранное", "Favorites"));

        var lists = FavoriteLists;
        var chooser = new ComboBox { ItemsSource = lists.Select(list => list.Name).ToArray(), Margin = new Thickness(0, 4, 0, 4) };
        DetailsPanel.Children.Add(chooser);
        var name = new TextBox { MaxLength = 80, Margin = new Thickness(0, 3, 0, 2) };
        DetailsPanel.Children.Add(Meta(T("Название списка", "List name")));
        DetailsPanel.Children.Add(name);
        var error = Meta("");
        var createRow = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        var newColor = new DatabaseFavoriteList();
        AddFavoriteButton(createRow, T("Создать список", "Create list"), () =>
        {
            var title = name.Text.Trim();
            if (title.Length == 0 || lists.Any(list => string.Equals(list.Name, title, StringComparison.OrdinalIgnoreCase)))
            {
                error.Text = T("Укажите уникальное название списка.", "Enter a unique list name.");
                return;
            }
            newColor.Name = title;
            lists.Add(newColor);
            _settings.DatabaseFavoriteLists[_database.Version] = lists;
            SaveFavorites();
            ShowFavoriteLists(title, false);
        });
        var createColor = new Button { Content = T("Цвет нового списка", "New list color") };
        createColor.Click += (_, _) =>
        {
            if (!PickFavoriteColor(newColor)) return;
            var preview = new StackPanel { Orientation = Orientation.Horizontal };
            preview.Children.Add(FavoriteColorMarker(newColor));
            preview.Children.Add(new TextBlock { Text = T("Цвет нового списка", "New list color") });
            createColor.Content = preview;
        };
        createRow.Children.Add(createColor);
        DetailsPanel.Children.Add(createRow);
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
            ShowFavoriteLists(title, false);
        };
        delete.Click += (_, _) =>
        {
            if (chooser.SelectedIndex < 0) return;
            var list = lists[chooser.SelectedIndex];
            var prompt = Meta(T($"Удалить список «{list.Name}» со всеми закладками?",
                $"Delete “{list.Name}” and its bookmarks?"));
            if (FavoriteDialog(T("Избранное", "Favorites"), prompt, T("Удалить", "Delete"), () => true,
                danger: true).ShowDialog() != true) return;
            lists.Remove(list);
            SaveFavorites();
            ShowFavoriteLists(null, false);
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
            _currentPage = () => ShowFavoriteLists(selectedName, false);
            var summary = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
            var color = new Button { Padding = new Thickness(5, 2, 5, 2), Content = T("Цвет", "Color") };
            DockPanel.SetDock(color, Dock.Right);
            color.Click += (_, _) =>
            {
                if (!PickFavoriteColor(list)) return;
                SaveFavorites();
                RenderList();
            };
            summary.Children.Add(color);
            var marker = FavoriteColorMarker(list);
            DockPanel.SetDock(marker, Dock.Left);
            summary.Children.Add(marker);
            var items = list.Entries.Count(favorite => favorite.Kind == DatabaseSearchKind.Items);
            var monsters = list.Entries.Count(favorite => favorite.Kind == DatabaseSearchKind.Monsters);
            var maps = list.Entries.Count(favorite => favorite.Kind == DatabaseSearchKind.Maps);
            summary.Children.Add(Meta(T($"Предметы {items} · Монстры {monsters} · Карты {maps}",
                $"Items {items} · Monsters {monsters} · Maps {maps}")));
            contents.Children.Add(summary);

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

    private bool PickFavoriteColor(DatabaseFavoriteList list, Window? owner = null)
    {
        var picker = new ColorPickerWindow(string.IsNullOrWhiteSpace(list.ColorHex) ? "#FF2863AA" : list.ColorHex)
        {
            Owner = owner ?? this, Topmost = owner?.Topmost ?? true
        };
        if (picker.ShowDialog() != true) return false;
        list.ColorHex = picker.SelectedHex;
        return true;
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
