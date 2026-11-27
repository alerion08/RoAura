using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RoAura.Controls;

/// <summary>A color field opens the same palette everywhere; cancellation leaves its binding untouched.</summary>
public sealed class ColorTextBox : TextBox
{
    private static readonly DependencyPropertyKey PreviewBrushPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "PreviewBrush", typeof(Brush), typeof(ColorTextBox), new PropertyMetadata(null));

    public static readonly DependencyProperty PreviewBrushProperty = PreviewBrushPropertyKey.DependencyProperty;

    public static Brush? GetPreviewBrush(DependencyObject element) => (Brush?)element.GetValue(PreviewBrushProperty);

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        if (ColorPickerWindow.TryParseHex(Text, out var color))
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            SetValue(PreviewBrushPropertyKey, brush);
        }
        else SetValue(PreviewBrushPropertyKey, null);
        base.OnTextChanged(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsReadOnly && IsEnabled) { e.Handled = true; OpenPalette(); }
        else base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && !IsReadOnly)
        { e.Handled = true; OpenPalette(); }
        else base.OnPreviewKeyDown(e);
    }

    private void OpenPalette()
    {
        var dialog = new ColorPickerWindow(Text) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        SetCurrentValue(TextProperty, dialog.SelectedHex);
        GetBindingExpression(TextProperty)?.UpdateSource();
    }
}
