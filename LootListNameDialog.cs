using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

internal static class LootListNameDialog
{
    internal static LootList? Create(Window owner, AppSettings settings, string server, LootList? existing = null)
    {
        string T(string key) => LocalizationService.Get(key);
        var dialog = new Window
        {
            Owner = owner, Title = T(existing is null ? "LootListCreate" : "LootListRename"), Width = 360, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
            Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = owner is DatabaseDetailWindow || owner.Topmost,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var body = new StackPanel { Margin = new Thickness(14) };
        var title = new TextBlock { Text = dialog.Title, Margin = new Thickness(0, 0, 0, 10) };
        title.SetResourceReference(FrameworkElement.StyleProperty, "RoSectionTitleStyle");
        body.Children.Add(title);
        body.Children.Add(new TextBlock { Text = T("LootListName") });
        var name = new TextBox { Text = existing?.Name ?? "", MaxLength = 80, Margin = new Thickness(0, 4, 0, 0) };
        body.Children.Add(name);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        error.SetResourceReference(TextBlock.ForegroundProperty, "UiForeground_B64B55");
        body.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(new Button { Content = T("Cancel"), IsCancel = true });
        var create = new Button { Content = T(existing is null ? "LootListCreate" : "Save"), IsDefault = true };
        create.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        actions.Children.Add(create);
        body.Children.Add(actions);
        var frame = new Border { Child = body, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "RoWindowBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "RoBorderBrush");
        dialog.Content = frame;
        dialog.Loaded += (_, _) => name.Focus();
        create.Click += (_, _) =>
        {
            var value = name.Text.Trim();
            if (value.Length == 0 || LootLists.ForServer(settings, server).Any(list =>
                    list != existing && string.Equals(list.Name, value, StringComparison.OrdinalIgnoreCase)))
            {
                error.Text = T("LootListUniqueName");
                name.Focus();
                return;
            }
            dialog.DialogResult = true;
        };
        if (dialog.ShowDialog() != true) return null;
        if (existing is not null)
        {
            existing.Name = name.Text.Trim();
            return existing;
        }
        var list = new LootList { Name = name.Text.Trim(), Server = GameServerProfile.Normalize(server) };
        settings.LootLists.Add(list);
        return list;
    }
    internal static bool Confirm(Window owner, string message, string action, bool centerContent = false)
    {
        var dialog = new Window { Owner = owner, Width = 360, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, Topmost = owner.Topmost };
        var body = new StackPanel { Margin = new Thickness(14) };
        var prompt = new TextBlock { Text = message, TextAlignment = centerContent ? TextAlignment.Center : TextAlignment.Left, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        prompt.SetResourceReference(FrameworkElement.StyleProperty, "RoSectionTitleStyle");
        body.Children.Add(prompt);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = centerContent ? HorizontalAlignment.Center : HorizontalAlignment.Right };
        actions.Children.Add(new Button { Content = LocalizationService.Get("Cancel"), IsCancel = true });
        var confirm = new Button { Content = action };
        confirm.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        confirm.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(confirm);
        body.Children.Add(actions);
        var frame = new Border { Child = body, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "RoWindowBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "RoBorderBrush");
        dialog.Content = frame;
        return dialog.ShowDialog() == true;
    }
}
