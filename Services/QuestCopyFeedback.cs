using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
namespace RoAura.Services;

internal static class QuestCopyFeedback
{
    private sealed class ResetState
    {
        internal object? Content;internal ControlTemplate? Template;internal Brush? Foreground;internal double MinWidth;
        internal DispatcherTimer? Timer;
    }
    private static readonly ConditionalWeakTable<Button,ResetState> States=new();
    internal static void Success(Button button,bool temporary=true)
    {
        var state=States.GetValue(button,b=>new ResetState {Content=b.Content,Template=b.Template,Foreground=b.Foreground,MinWidth=b.MinWidth});
        state.Timer?.Stop();
        button.MinWidth=Math.Max(64,button.ActualWidth);button.Content="copied";
        ColorState(button,Brushes.ForestGreen);
        if(!temporary)return; // tracker host owns its per-entry expiry across rebuilds
        state.Timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(4)};
        state.Timer.Tick+=(_,_)=>
        {
            state.Timer.Stop();button.Content=state.Content;button.Template=state.Template;
            button.Foreground=state.Foreground;button.MinWidth=state.MinWidth;States.Remove(button);
        };
        state.Timer.Start();
    }
    internal static void ColorState(Button button,Brush fill)
    {
        button.Foreground=Brushes.White;
        ApplyTemplate(button,fill);
    }
    internal static void ApplyTemplate(Button button,Brush fill)
    {
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.PaddingProperty,new Binding("Padding") {RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});
        border.SetValue(Border.BackgroundProperty,fill);border.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        var content=new FrameworkElementFactory(typeof(TextBlock));
        content.SetBinding(TextBlock.TextProperty,new Binding("Content") {RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});
        content.SetBinding(TextBlock.ForegroundProperty,new Binding("Foreground") {RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(content);button.Template=new ControlTemplate(typeof(Button)) {VisualTree=border};
    }
}
