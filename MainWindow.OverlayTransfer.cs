using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RoAura.Services;

namespace RoAura;

public partial class MainWindow
{
    private const string OverlayFileExtension = ".roaura-overlay.json";

    private void ExportOverlays_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<OverlayTransferKind>(tag, out var kind)) return;
        var dialog = new SaveFileDialog
        {
            Filter = OverlayFileFilter(), DefaultExt = OverlayFileExtension,
            FileName = $"RoAura-{tag.ToLowerInvariant()}{OverlayFileExtension}"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            OverlayTransfer.Write(dialog.FileName, Settings, kind);
            SetStatus(T("Оверлеи экспортированы.", "Overlays exported."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            ReportOverlayTransferError(ex);
        }
    }

    private void ImportOverlays_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<OverlayTransferKind>(tag, out var kind)) return;
        var dialog = new OpenFileDialog { Filter = OverlayFileFilter(), CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var count = OverlayTransfer.Read(dialog.FileName, Settings, kind);
            Settings.SyncCooldownGroups();
            Settings.SyncBuffPanelGroups();
            if (kind == OverlayTransferKind.Cooldown) RebuildCooldownGroupFilterItems();
            _overlay.RefreshCharacterScopes();
            _overlay.RefreshPanelLayers();
            SaveSettings();
            SetStatus(T($"Добавлено оверлеев: {count}.", $"Overlays added: {count}."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            ReportOverlayTransferError(ex);
        }
    }

    private string OverlayFileFilter() => T("Набор оверлеев RoAura", "RoAura overlay package") + " (*.json)|*.json";

    private void ReportOverlayTransferError(Exception error)
    {
        DebugLog.Write($"OVERLAY TRANSFER: {error.GetType().Name}");
        MessageBox.Show(this,
            T("Не удалось обработать файл. Проверьте, что файл доступен, а при импорте выбран набор для этого раздела.",
              "Could not process the file. Check access and, for imports, choose a package for this section."),
            "RoAura", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
