using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

internal sealed class FarmingSessionTimeline : Grid
{
    public FarmingSessionTimeline(FarmingSession session)
    {
        Margin = new Thickness(10);
        if (session.Intervals.Count == 0)
        {
            Children.Add(new TextBlock { Text = LocalizationService.Get("SessionTimelineEmpty"), TextWrapping = TextWrapping.Wrap });
            return;
        }

        AddSeries("QFactBaseExp", Brushes.RoyalBlue, point => point.BaseExperience);
        AddSeries("QFactJobExp", Brushes.MediumSeaGreen, point => point.JobExperience);
        AddSeries("SessionTimelineZeny", Brushes.DarkGoldenrod, point => point.Zeny);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var caption = new TextBlock { Text = LocalizationService.Get("SessionTimelineAxis"), HorizontalAlignment = HorizontalAlignment.Center };
        SetRow(caption, 3);
        Children.Add(caption);

        void AddSeries(string label, Brush brush, Func<FarmingInterval, long> select)
        {
            var row = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition());
            var series = new Series(session, LocalizationService.Get(label), brush, select);
            SetRow(series, row);
            Children.Add(series);
        }
    }

    private sealed class Series(FarmingSession session, string label, Brush brush, Func<FarmingInterval, long> select) : FrameworkElement
    {
        private readonly double _count = Math.Max(Math.Max(0, session.ActiveTicks - 1) / FarmingInterval.Ticks,
            session.Intervals.Keys.Max()) + 1d;
        private readonly long _max = session.Intervals.Values.Max(select);
        private const double Left = 8;
        private const double Top = 20;
        private const double Bottom = 16;
        private double PlotWidth => Math.Max(1, ActualWidth - 2 * Left);

        protected override void OnRender(DrawingContext drawing)
        {
            var baseline = Math.Max(Top, ActualHeight - Bottom);
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            drawing.DrawLine(new Pen(SystemColors.GrayTextBrush, 1), new Point(Left, baseline),
                new Point(Left + PlotWidth, baseline));
            DrawText(drawing, $"{label}   ·   {_max:N0}", Left, 0);
            DrawText(drawing, "0", Left, baseline);
            var duration = TimeSpan.FromTicks(session.ActiveTicks);
            var end = $"{(long)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
            DrawText(drawing, end, Math.Max(Left, ActualWidth - 85), baseline);

            var step = PlotWidth / _count;
            foreach (var (index, interval) in session.Intervals)
            {
                var value = select(interval);
                if (value == 0) continue;
                var x = Left + index * step;
                var height = (baseline - Top) * value / _max;
                drawing.DrawRectangle(brush, null, new Rect(x, baseline - height, Math.Max(1, step * .8), height));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var index = (long)Math.Clamp(Math.Floor((e.GetPosition(this).X - Left) / PlotWidth * _count), 0, _count - 1);
            var minute = index * 5;
            var value = select(session.Intervals.GetValueOrDefault(index));
            var detail = $"{minute}–{minute + 5} · {label} · {value:N0}";
            if (ToolTip is ToolTip tooltip) tooltip.Content = detail;
            else ToolTip = new ToolTip { Content = detail };
        }

        private void DrawText(DrawingContext drawing, string text, double x, double y)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, SystemColors.ControlTextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(formatted, new Point(x, y));
        }
    }
}
