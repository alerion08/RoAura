using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoAura.Services;

namespace RoAura.Controls;

/// <summary>A color field opens the same palette everywhere; cancellation leaves its binding untouched.</summary>
public sealed class ColorTextBox : TextBox
{
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
