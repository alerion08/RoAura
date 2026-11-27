using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    private void AddLootListAction(DatabaseItem item, Panel actions)
    {
        var button = new Button { Content = LocalizationService.Get("LootListAddAction"),
            HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4, 2, 4, 2) };
        var options = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
        var panel = new Border { Child = options, Visibility = Visibility.Collapsed,
            CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
        panel.SetResourceReference(Border.BackgroundProperty, "RoPanelBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "RoSoftBorderBrush");
        void UpdateButton()
        {
            if (LootLists.ForServer(_settings, _database.Version).Any(list => list.Items.Any(row => row.ItemId == item.Id)))
            {
                button.SetResourceReference(StyleProperty, "ActiveItemCardButtonStyle");
                button.Foreground = System.Windows.Media.Brushes.White;
            }
            else
            {
                button.ClearValue(StyleProperty);
                button.ClearValue(Control.ForegroundProperty);
            }
        }

        void UpdateOptions()
        {
            options.Children.Clear();
            foreach (var list in LootLists.ForServer(_settings, _database.Version))
            {
                var option = new CheckBox { Content = list.Name, Margin = new Thickness(4, 4, 4, 5),
                    IsChecked = list.Items.Any(row => row.ItemId == item.Id) };
                option.Click += (_, _) =>
                {
                    if (option.IsChecked == true) LootLists.AddItem(list, item.Id, item.Name);
                    else if (list.Items.FirstOrDefault(row => row.ItemId == item.Id) is { } existing)
                        list.Items.Remove(existing);
                    (Application.Current as App)?.LootListsChanged();
                    UpdateButton();
                };
                options.Children.Add(option);
            }
            var create = new Button { Content = LocalizationService.Get("LootListCreateMenu"), Margin = new Thickness(0, 4, 0, 0) };
            create.Click += (_, _) =>
            {
                if (LootListNameDialog.Create(this, _settings, _database.Version) is not { } list) return;
                LootLists.AddItem(list, item.Id, item.Name);
                (Application.Current as App)?.LootListsChanged();
                UpdateButton();
                UpdateOptions();
            };
            options.Children.Add(create);
        }

        button.Click += (_, _) =>
        {
            if (panel.Visibility == Visibility.Visible) { panel.Visibility = Visibility.Collapsed; return; }
            UpdateOptions();
            panel.Visibility = Visibility.Visible;
        };
        UpdateButton();
        actions.Children.Add(button);
        DetailsPanel.Children.Add(panel);
    }
}
