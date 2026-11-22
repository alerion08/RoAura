using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace RoAura.Controls;

public sealed class ExpirationGlow : FrameworkElement
{
    public static readonly DependencyProperty GlowBrushProperty = DependencyProperty.Register(
        nameof(GlowBrush), typeof(Brush), typeof(ExpirationGlow),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender, UpdateHalo));

    public static readonly DependencyProperty GlowWidthProperty = DependencyProperty.Register(
        nameof(GlowWidth), typeof(double), typeof(ExpirationGlow),
        new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(int), typeof(ExpirationGlow),
        new PropertyMetadata(0, (sender, _) => ((ExpirationGlow)sender).RefreshAnimation()));

    public static readonly DependencyProperty IsUrgentProperty = DependencyProperty.Register(
        nameof(IsUrgent), typeof(bool), typeof(ExpirationGlow),
        new PropertyMetadata(false, (sender, args) =>
        {
            var glow = (ExpirationGlow)sender;
            if (glow.Mode == 2 && args.NewValue is true) glow.RefreshAnimation();
        }));

    public bool IsUrgent { get => (bool)GetValue(IsUrgentProperty); set => SetValue(IsUrgentProperty, value); }
    public int Mode { get => (int)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }

    private static readonly DoubleAnimation Flash = CreateFlash();
    private static readonly DoubleAnimation Pulse = CreatePulse();

    public Brush GlowBrush { get => (Brush)GetValue(GlowBrushProperty); set => SetValue(GlowBrushProperty, value); }
    public double GlowWidth { get => (double)GetValue(GlowWidthProperty); set => SetValue(GlowWidthProperty, value); }

    public ExpirationGlow()
    {
        IsHitTestVisible = false;
        SetHalo();
        IsVisibleChanged += (_, _) => RefreshAnimation();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0 || GlowBrush is null) return;

        // The pen is centered on the original icon bounds.
        var stroke = new Pen(GlowBrush, GlowWidth);
        drawingContext.DrawRoundedRectangle(null, stroke, new Rect(RenderSize), 3, 3);
    }

    internal void RefreshAnimation() =>
        BeginAnimation(OpacityProperty, IsVisible ? Mode switch { 0 => Pulse, 2 => Flash, _ => null } : null);

    private static DoubleAnimation CreateFlash()
    {
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450));
        animation.Freeze();
        return animation;
    }

    private static DoubleAnimation CreatePulse()
    {
        var animation = new DoubleAnimation(.35, 1, TimeSpan.FromSeconds(.7))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Freeze();
        return animation;
    }

    private static void UpdateHalo(DependencyObject sender, DependencyPropertyChangedEventArgs _) =>
        ((ExpirationGlow)sender).SetHalo();

    private void SetHalo()
    {
        var halo = new DropShadowEffect
        {
            Color = (GlowBrush as SolidColorBrush)?.Color ?? Colors.Orange,
            ShadowDepth = 0,
            BlurRadius = 18,
            Opacity = .95
        };
        halo.Freeze();
        Effect = halo;
    }
}
