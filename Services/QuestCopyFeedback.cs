using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace RoAura.Services;

internal static class QuestCopyFeedback
{
    internal static void Success(Button button)
    {
        // The ordinary theme's hover fill would hide green while the mouse stays over navi.
        button.MinWidth=Math.Max(36,button.ActualWidth);
        button.Content="✓";button.Foreground=Brushes.White;
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty,Brushes.ForestGreen);
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        var content=new FrameworkElementFactory(typeof(TextBlock));
        content.SetValue(TextBlock.TextProperty,"✓");
        content.SetValue(TextBlock.ForegroundProperty,Brushes.White);
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(content);
        button.Template=new ControlTemplate(typeof(Button)) {VisualTree=border};
    }
}
