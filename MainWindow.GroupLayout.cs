using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MainWindow
{
    private void BuffDirection_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (sender is not ComboBox { IsLoaded: true }) return;
        Dispatcher.BeginInvoke(() =>
        {
            _overlay.RefreshLayoutPreviewPositions();
            RequestSave();
        });
    }

    private string? _openedCooldownDirection;

    private void CooldownDirection_DropDownOpened(object sender, EventArgs args) =>
        _openedCooldownDirection = (sender as ComboBox)?.SelectedValue as string;

    private void CooldownDirection_DropDownClosed(object sender, EventArgs args)
    {
        if (_openedCooldownDirection is not null && sender is ComboBox { SelectedValue: string direction } &&
            direction != _openedCooldownDirection)
            ApplyCooldownGroupLayout_Click(sender, new RoutedEventArgs());
        _openedCooldownDirection = null;
    }

    private void ConfigureEquipmentGroup_Click(object sender, RoutedEventArgs args)
    {
        if (EquipmentGrid.SelectedItem is not EquipmentTracker item) return;
        var existing = Settings.EquipmentGroupLayouts.GetValueOrDefault(item.GroupNumber);
        var number = new TextBox { Text = (item.GroupNumber > 0 ? item.GroupNumber : 1).ToString() };
        var size = new TextBox { Text = (existing?.IconSize ?? item.IconSize).ToString(CultureInfo.CurrentCulture) };
        var gap = new TextBox { Text = (existing?.Gap ?? 6).ToString(CultureInfo.CurrentCulture) };
        var direction = new ComboBox { SelectedIndex = existing?.Vertical == true ? 1 : 0 };
        direction.Items.Add(LocalizationService.Get("Horizontal"));
        direction.Items.Add(LocalizationService.Get("Vertical"));
        direction.SelectedIndex = existing?.Vertical == true ? 1 : 0;
        var enabled = new CheckBox { Content = LocalizationService.Get("EquipmentApplyGroupLayout"),
            IsChecked = true, Margin = new Thickness(0, 5, 0, 10) };
        var panel = new StackPanel { Margin = new Thickness(16) };
        AddField("Group", number);
        panel.Children.Add(enabled);
        AddField("IconSize", size);
        AddField("IconGap", gap);
        AddField("LayoutDirection", direction);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var confirm = new Button { Content = LocalizationService.Get("Save"), IsDefault = true };
        var cancel = new Button { Content = LocalizationService.Get("Cancel"), IsCancel = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(confirm);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        var dialog = new Window { Owner = this, Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = LocalizationService.Get("EquipmentGroupLayoutTitle"), Content = panel };
        dialog.SetResourceReference(BackgroundProperty, "RoWindowBrush");
        dialog.SetResourceReference(ForegroundProperty, "RoTextBrush");
        number.TextChanged += (_, _) =>
        {
            if (!int.TryParse(number.Text, out var group) ||
                !Settings.EquipmentGroupLayouts.TryGetValue(group, out var layout)) return;
            size.Text = layout.IconSize.ToString(CultureInfo.CurrentCulture);
            gap.Text = layout.Gap.ToString(CultureInfo.CurrentCulture);
            direction.SelectedIndex = layout.Vertical ? 1 : 0;
        };
        confirm.Click += (_, _) =>
        {
            if (!int.TryParse(number.Text, out var group) || group is < 0 or > 999 ||
                !double.TryParse(size.Text, out var iconSize) || !double.IsFinite(iconSize) || iconSize is < 20 or > 180 ||
                !double.TryParse(gap.Text, out var spacing) || !double.IsFinite(spacing) || spacing is < 0 or > 100)
            {
                error.Text = LocalizationService.Get("EquipmentGroupLayoutInvalid");
                return;
            }
            if (group > 0)
            {
                if (enabled.IsChecked == true)
                    Settings.EquipmentGroupLayouts[group] = new EquipmentGroupLayout(iconSize, spacing, direction.SelectedIndex == 1);
                else
                    Settings.EquipmentGroupLayouts.Remove(group);
            }
            item.GroupNumber = group;
            _overlay.RefreshPanelLayers();
            SaveSettings();
            dialog.DialogResult = true;
        };
        dialog.ShowDialog();

        void AddField(string key, Control field)
        {
            panel.Children.Add(new Label { Content = LocalizationService.Get(key), Target = field });
            panel.Children.Add(field);
        }
    }
}
