using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RoAura.Models;

namespace RoAura.Services;

internal static class TableLayout
{
    internal static void Attach(DataGrid grid, AppSettings settings, string key, Action save,
        DataGridColumn? frozen = null, bool fitToViewport = false)
    {
        if (frozen is not null) frozen.DisplayIndex = 0;
        grid.FrozenColumnCount = 1;
        var defaults = grid.Columns.Select(column => column.Width).ToArray();

        void Restore()
        {
            var widths = settings.TableColumnWidths.GetValueOrDefault(key);
            for (var index = 0; index < defaults.Length; index++)
            {
                var width = widths?.Length == defaults.Length ? widths[index] : 0;
                grid.Columns[index].Width = double.IsFinite(width) && width is >= 16 and <= 4096
                    ? new DataGridLength(width, fitToViewport ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel)
                    : defaults[index];
            }
        }

        grid.Loaded += (_, _) => Restore();
        grid.DataContextChanged += (_, _) => Restore();
        grid.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, args) =>
        {
            if (args.Canceled || args.OriginalSource is not Thumb { TemplatedParent: DataGridColumnHeader }) return;
            settings.TableColumnWidths[key] = grid.Columns.Select(column => column.ActualWidth).ToArray();
            save();
        }), handledEventsToo: true);
        Restore();
    }
}
