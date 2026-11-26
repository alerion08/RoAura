using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace RoAura.Services;

internal static class GoldGoblinSplitLayout
{
    internal static Action Attach(GridSplitter splitter, ColumnDefinition editor, Func<double> load,
        Action<double> store, Action save)
    {
        void Restore()
        {
            var ratio = load();
            if (splitter.Parent is not Grid grid) return;
            if (!double.IsFinite(ratio) || ratio <= 0 || ratio >= 1) ratio = .375;
            grid.ColumnDefinitions[0].Width = new GridLength(1 - ratio, GridUnitType.Star);
            editor.Width = new GridLength(ratio, GridUnitType.Star);
        }

        splitter.Loaded += (_, _) => Restore();
        splitter.DataContextChanged += (_, _) => Restore();
        void Remember()
        {
            if (splitter.Parent is not Grid grid) return;
            var left = grid.ColumnDefinitions[0].ActualWidth;
            var right = editor.ActualWidth;
            if (left <= 0 || right <= 0) return;
            store(right / (left + right));
            save();
        }
        splitter.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, args) =>
        {
            if (!args.Canceled) Remember();
        }), handledEventsToo: true);
        splitter.PreviewKeyUp += (_, args) =>
        {
            if (args.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right) Remember();
        };
        Restore();
        return Restore;
    }
}
