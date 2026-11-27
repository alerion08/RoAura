using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    internal void RefreshSmokePalette() => UpdateDatabasePalette();

    private void UpdateDatabasePalette() =>
        DatabasePresentation.ApplyPalette(this, AppearanceService.Resolve(_settings.ApplicationTheme, _settings.GameOverlayTheme, true) == "Dark");

    private FrameworkElement EntityIcon(byte[]? sprite, string glyph, string? imageUrl = null)
    {
        var image = DatabasePresentation.Icon(sprite, glyph, 46);
        if (sprite is not { Length: > 0 } && Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) && image is Grid grid)
            grid.Children.Add(new Image { Source = new BitmapImage(uri), Stretch = Stretch.Uniform });
        return new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top, Child = image };
    }

    private void AddEntityHeader(string name, string meta, byte[]? sprite, string glyph, string? imageUrl = null)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 40, 12) };
        header.Children.Add(EntityIcon(sprite, glyph, imageUrl));
        var title = new StackPanel();
        title.Children.Add(Heading(name, 23));
        title.Children.Add(Meta(meta));
        header.Children.Add(title);
        DetailsPanel.Children.Add(header);
    }

    private void AddMetrics(params (string Label, string Value)[] values)
    {
        var grid = new UniformGrid { Rows = 1, Margin = new Thickness(-3, 0, -3, 0) };
        foreach (var (label, value) in values)
        {
            var body = new StackPanel();
            var legend = Meta(label);
            legend.Margin = new Thickness(0);
            body.Children.Add(legend);
            body.Children.Add(new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var tile = new Border { Child = body, CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(3) };
            tile.SetResourceReference(Border.BackgroundProperty, "DatabaseSoft");
            grid.Children.Add(tile);
        }
        DetailsPanel.Children.Add(grid);
    }

    private void AddNavigationRow(Panel parent, string caption, string? command)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 3) };
        if (!string.IsNullOrWhiteSpace(command))
        {
            var navi = new Button { Content = "Navi", ToolTip = command, Height = 26, MinHeight = 0,
                Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(5, 0, 0, 0) };
            DockPanel.SetDock(navi, Dock.Right);
            row.Children.Add(navi);
            navi.Click += (_, _) =>
            {
                navi.Content = TryCopyToClipboard(command) ? "✓" : "!";
                navi.ToolTip = Equals(navi.Content, "✓") ? T("Скопировано", "Copied") : T("Не удалось скопировать", "Could not copy");
            };
        }
        row.Children.Add(Meta(caption));
        parent.Children.Add(row);
    }

    private void AddReferenceRow(string title, string subtitle, string value, byte[]? sprite, string glyph,
        Action? action, Panel? parent = null)
    {
        var row = new Grid();
        if (glyph.Length > 0)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            var icon = DatabasePresentation.Icon(sprite, glyph, 18);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(icon);
        }
        var textColumn = row.ColumnDefinitions.Count;
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var meta = Meta(subtitle);
            meta.Margin = new Thickness(0);
            labels.Children.Add(meta);
        }
        Grid.SetColumn(labels, textColumn);
        row.Children.Add(labels);
        if (!string.IsNullOrWhiteSpace(value))
        {
            var metric = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, MaxWidth = 102,
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(metric, textColumn + 1);
            row.Children.Add(metric);
        }
        var container = parent ?? DetailsPanel;
        if (action is null)
        {
            var line = new Border { Child = row, Padding = new Thickness(7, 5, 7, 5), MinHeight = 31,
                BorderThickness = new Thickness(0, 0, 0, 1) };
            line.SetResourceReference(Border.BorderBrushProperty, "DatabaseLine");
            container.Children.Add(line);
            return;
        }
        var button = new Button { Content = row };
        button.SetResourceReference(StyleProperty, "DatabaseLinkRow");
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        button.Click += (_, _) => action();
        container.Children.Add(button);
    }
}
