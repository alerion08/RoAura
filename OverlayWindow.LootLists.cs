using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class OverlayWindow
{
    internal void RefreshLootListTracker()
    {
        var options = _settings.LootListTracker;
        LootListTrackerPanel.Children.Clear();
        if (!options.Enabled) return;

        var fontSize = Limit(options.TextSize, 14, 10, 32);
        var width = Limit(options.Width, 300, 170, 680);
        var gap = Limit(options.Gap, 6, 0, 40);
        var opacity = Limit(options.BackgroundOpacityPercent, 75, 0, 100) / 100;
        var foreground = ColorPickerWindow.TryParseHex(options.TextColor, out var text) ? text : Colors.White;
        var background = ColorPickerWindow.TryParseHex(options.BackgroundColor, out var fill)
            ? fill : Color.FromRgb(18, 28, 48);
        var brush = new SolidColorBrush(background) { Opacity = opacity };
        brush.Freeze();
        var ink = new SolidColorBrush(foreground);
        ink.Freeze();

        var completeInk = new SolidColorBrush(background.G + background.R / 2 + background.B / 4 >= 210 && opacity >= 0.6
            ? Color.FromRgb(0, 102, 55) : Color.FromRgb(139, 239, 166));
        completeInk.Freeze();
        var rowStyle = new Style(typeof(TextBlock));
        rowStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, ink));
        rowStyle.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(nameof(LootListItem.IsComplete)),
            Value = true,
            Setters = { new Setter(TextBlock.ForegroundProperty, completeInk) }
        });

        LootListTrackerPanel.Orientation = options.Horizontal ? Orientation.Horizontal : Orientation.Vertical;
        var lists = LootLists.ForServer(_settings, _settings.ServerProfile)
            .Select(list => (list.Name, Items: LootLists.TrackerItems(list).ToArray()))
            .Where(list => list.Items.Length > 0).ToArray();
        if (IsLayoutEditing && lists.Length == 0)
            lists = [(LocalizationService.Get("LootListTrackerTitle"),
                [new LootListItem { Name = LocalizationService.Get("Item"), Target = 25, Acquired = 5 }])];
        foreach (var list in lists)
        {
            var active = list.Items;
            var rows = new StackPanel();
            rows.Children.Add(new TextBlock { Text = list.Name, Foreground = ink, FontSize = fontSize,
                FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            for (var index = 0; index < active.Length; index++)
            {
                var item = active[index];
                var name = item.Name == $"Item #{item.ItemId}" && Application.Current is App app
                    ? app.ResolveLootListItemName(item.ItemId) ?? item.Name : item.Name;
                var line = new TextBlock { DataContext = item, Style = rowStyle, FontSize = fontSize, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0) };
                var progress = new MultiBinding { StringFormat = "{0}/{1}" };
                progress.Bindings.Add(new Binding(nameof(LootListItem.Acquired)) { Source = item });
                progress.Bindings.Add(new Binding(nameof(LootListItem.Target)) { Source = item });
                var counts = new System.Windows.Documents.Run();
                counts.SetBinding(System.Windows.Documents.Run.TextProperty, progress);
                line.Inlines.Add(new System.Windows.Documents.Run($"{index + 1}. {name} "));
                line.Inlines.Add(counts);
                rows.Children.Add(line);
            }

            LootListTrackerPanel.Children.Add(new Border
            {
                Child = rows, Width = width, Padding = new Thickness(9, 7, 9, 8),
                CornerRadius = new CornerRadius(8), Background = brush,
                Margin = new Thickness(0, 0, gap, gap)
            });
        }
        UpdateLootListTrackerPosition();
    }

    private void LootListTrackerLayer_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateLootListTrackerPosition();

    internal void UpdateLootListTrackerPosition()
    {
        var options = _settings.LootListTracker;
        var viewportWidth = LootListTrackerLayer.ActualWidth;
        var viewportHeight = LootListTrackerLayer.ActualHeight;
        var x = Limit(options.X, 12, 0, 10000);
        var y = Limit(options.Y, 420, 0, 10000);
        if (viewportWidth > 0 && viewportHeight > 0)
        {
            // Let cards wrap before clamping the group back inside the game client.
            LootListTrackerPanel.MaxWidth = viewportWidth;
            LootListTrackerPanel.MaxHeight = viewportHeight;
            LootListTrackerPanel.Measure(new Size(viewportWidth, viewportHeight));
            var bounds = LootListTrackerPanel.DesiredSize;
            x = Math.Min(x, Math.Max(0, viewportWidth - bounds.Width));
            y = Math.Min(y, Math.Max(0, viewportHeight - bounds.Height));
        }
        Canvas.SetLeft(LootListTrackerPanel, x);
        Canvas.SetTop(LootListTrackerPanel, y);
    }

    private static double Limit(double value, double fallback, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
