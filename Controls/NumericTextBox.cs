using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using RoAura.Services;

namespace RoAura.Controls;

/// <summary>Keep the user's text separate from the bounded model value.</summary>
public sealed class NumericTextBox : TextBox
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumericTextBox),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged, null, false, UpdateSourceTrigger.Explicit));

    private bool _updatingText;
    private bool _hasDraft;

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public NumericTextBox()
    {
        RefreshText();
        DataContextChanged += (_, _) => CancelInput();
    }

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var editor = (NumericTextBox)sender;
        // Packet updates, sliders and other source notifications must not
        // replace a half-typed number or move the caret during editing.
        if (!editor._hasDraft) editor.RefreshText();
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        if (!_updatingText) _hasDraft = true;
        base.OnTextChanged(e);
    }

    public bool CommitInput()
    {
        if (!_hasDraft) return true;
        var binding = GetBindingExpression(ValueProperty);
        // Accept both decimal separators without treating either as a
        // thousands separator; reject NaN/Infinity before they reach layout.
        if (!double.TryParse(Text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
        {
            if (binding is not null)
                Validation.MarkInvalid(binding, new ValidationError(new ExceptionValidationRule(),
                    binding, LocalizationService.Get("NumericInputInvalid"), null));
            return false;
        }

        if (binding is not null) Validation.ClearInvalid(binding);
        SetCurrentValue(ValueProperty, number);
        binding?.UpdateSource();
        // Read the actual bounded value even if the model was already at a
        // limit and its setter did not raise PropertyChanged this time.
        binding?.UpdateTarget();
        _hasDraft = false;
        RefreshText();
        return true;
    }

    public void CancelInput()
    {
        var binding = GetBindingExpression(ValueProperty);
        if (binding is not null) Validation.ClearInvalid(binding);
        _hasDraft = false;
        RefreshText();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        CommitInput();
        base.OnLostFocus(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitInput();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelInput();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private void RefreshText()
    {
        _updatingText = true;
        try { SetCurrentValue(TextProperty, Value.ToString("G", CultureInfo.CurrentCulture)); }
        finally { _updatingText = false; }
    }
}
