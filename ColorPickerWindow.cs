using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RoAura.Services;

namespace RoAura;

public sealed class ColorPickerWindow : Window
{
    private readonly TextBox _hex = new() { MinWidth = 160, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Border _preview = new() { Height = 44, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly Slider[] _channels = new Slider[4];
    private readonly TextBlock[] _values = new TextBlock[4];
    private bool _updating;
    public string SelectedHex { get; private set; } = "#FFFFFFFF";

    public ColorPickerWindow(string initial)
    {
        Title = LocalizationService.Get("ColorPickerTitle");
        Width = 430; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "RoWindowBrush");
        var root = new StackPanel { Margin = new Thickness(18) };
        Content = root;
        var palette = new UniformGrid { Columns = 12 };
        foreach (var r in new byte[] { 0, 85, 170, 255 })
        foreach (var g in new byte[] { 0, 85, 170, 255 })
        foreach (var b in new byte[] { 0, 128, 255 })
        {
            var color = Color.FromRgb(r, g, b);
            var button = new Button { Background = new SolidColorBrush(color), Height = 24, MinHeight = 24,
                Padding = new Thickness(0), Margin = new Thickness(2), ToolTip = color.ToString() };
            // Swatches must retain their actual color on hover/press.
            button.Template = SwatchTemplate();
            button.Click += (_, _) => SetColor(Color.FromArgb((byte)_channels[0].Value, color.R, color.G, color.B));
            palette.Children.Add(button);
        }
        root.Children.Add(palette);
        root.Children.Add(_preview);
        var names = new[] { "A", "R", "G", "B" };
        for (var i = 0; i < 4; i++)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.Children.Add(new TextBlock { Text = names[i], VerticalAlignment = VerticalAlignment.Center });
            var slider = new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
            _channels[i] = slider; Grid.SetColumn(slider, 1); row.Children.Add(slider);
            var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            _values[i] = value; Grid.SetColumn(value, 2); row.Children.Add(value);
            slider.ValueChanged += (_, _) => { if (!_updating) SetColor(CurrentColor()); };
            root.Children.Add(row);
        }
        root.Children.Add(new TextBlock { Text = "HEX (#RRGGBB / #AARRGGBB)", Margin = new Thickness(0, 8, 0, 4) });
        root.Children.Add(_hex); root.Children.Add(_error);
        _hex.TextChanged += (_, _) =>
        {
            if (_updating) return;
            if (TryParseHex(_hex.Text, out var color)) SetColor(color, updateHex: false);
            else _error.Text = LocalizationService.Get("ColorPickerInvalid");
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = new Button { Content = LocalizationService.Get("Cancel"), IsCancel = true };
        var apply = new Button { Content = LocalizationService.Get("ColorPickerApply"), IsDefault = true };
        apply.Click += (_, _) =>
        {
            if (!TryParseHex(_hex.Text, out var color)) { _error.Text = LocalizationService.Get("ColorPickerInvalid"); return; }
            SelectedHex = color.ToString(CultureInfo.InvariantCulture);
            DialogResult = true;
        };
        actions.Children.Add(cancel); actions.Children.Add(apply); root.Children.Add(actions);
        var start = Colors.White;
        try { if (ColorConverter.ConvertFromString(initial) is Color parsed) start = parsed; } catch (FormatException) { }
        SetColor(start);
    }

    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        var hex = text?.Trim().TrimStart('#');
        if (hex is null || (hex.Length != 6 && hex.Length != 8) ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bits)) return false;
        color = Color.FromArgb(hex.Length == 6 ? (byte)255 : (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits);
        return true;
    }

    private Color CurrentColor() => Color.FromArgb((byte)_channels[0].Value, (byte)_channels[1].Value, (byte)_channels[2].Value, (byte)_channels[3].Value);
    private void SetColor(Color color, bool updateHex = true)
    {
        _updating = true;
        try
        {
            byte[] values = [color.A, color.R, color.G, color.B];
            for (var i = 0; i < 4; i++) { _channels[i].Value = values[i]; _values[i].Text = values[i].ToString(CultureInfo.InvariantCulture); }
            _preview.Background = new SolidColorBrush(color);
            SelectedHex = color.ToString(CultureInfo.InvariantCulture);
            if (updateHex) _hex.Text = SelectedHex;
            _error.Text = "";
        }
        finally { _updating = false; }
    }

    private static ControlTemplate SwatchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
