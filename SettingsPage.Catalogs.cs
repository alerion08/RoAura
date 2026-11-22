using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class SettingsPage
{
    private bool _catalogBusy;

    internal void ShowCatalogSettings()
    {
        SettingsTabs.SelectedIndex = 0;
        CatalogSettings.BringIntoView();
    }

    private string CatalogText(string ru, string en) => _observed?.Language == "en" ? en : ru;

    private void RefreshCatalogRows()
    {
        CatalogHeading.Text = CatalogText("Локальные базы данных", "Local databases");
        CatalogRows.Children.Clear();
        foreach (var profile in new[] { GameServerProfile.RagnarokPlus, GameDatabaseVersion.SpringRo })
        {
            var installed = ServerCatalogDownload.IsInstalled(profile);
            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            var status = installed ? CatalogText("Загружена", "Installed") : CatalogText("Не загружена", "Not installed");
            row.Children.Add(new TextBlock { Text = $"{profile} — {status}", MinWidth = 180,
                VerticalAlignment = VerticalAlignment.Center });
            var action = new Button
            {
                Content = installed ? CatalogText("Обновить", "Update") : CatalogText("Загрузить", "Download"),
                IsEnabled = !_catalogBusy,
                Margin = new Thickness(4, 0, 0, 0)
            };
            action.Click += async (_, _) => await InstallCatalogAsync(profile);
            row.Children.Add(action);
            if (installed)
            {
                var remove = new Button { Content = CatalogText("Удалить ×", "Remove ×"), IsEnabled = !_catalogBusy,
                    Margin = new Thickness(4, 0, 0, 0) };
                remove.Click += (_, _) => RemoveCatalog(profile);
                row.Children.Add(remove);
            }
            CatalogRows.Children.Add(row);
        }
    }

    private async Task InstallCatalogAsync(string profile)
    {
        if (_catalogBusy || ServerCatalogDownload.ServerKey(profile) is not { } key) return;
        _catalogBusy = true;
        RefreshCatalogRows();
        CatalogStatus.Text = CatalogText("Проверяем каталог…", "Checking catalog…");
        var progress = new Progress<(int Percent, long SizeBytes)>(p =>
        {
            if (_catalogBusy) CatalogStatus.Text = $"{profile} · {p.Percent}% · {p.SizeBytes / 1048576d:N0} MiB";
        });
        try
        {
            var changed = await Task.Run(() => ServerCatalogDownload.InstallAsync(key, progress, CancellationToken.None));
            if (changed) (Application.Current as App)?.ServerCatalogUpdated(profile);
            CatalogStatus.Text = changed ? CatalogText("Данные загружены.", "Data installed.") :
                CatalogText("Установлена актуальная версия.", "The catalog is up to date.");
        }
        catch (CatalogUnavailableException)
        {
            CatalogStatus.Text = CatalogText("Каталог пока не опубликован.", "The catalog has not been published yet.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"CATALOG DOWNLOAD ERROR: {ex.Message}");
            CatalogStatus.Text = CatalogText("Не удалось загрузить каталог.", "Could not download the catalog.");
        }
        finally
        {
            _catalogBusy = false;
            RefreshCatalogRows();
        }
    }

    private void RemoveCatalog(string profile)
    {
        if (_catalogBusy || ServerCatalogDownload.ServerKey(profile) is not { } key) return;
        try
        {
            ServerCatalogDownload.Remove(key);
            (Application.Current as App)?.ServerCatalogUpdated(profile);
            CatalogStatus.Text = CatalogText("Локальная база удалена.", "Local database removed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"CATALOG REMOVE ERROR: {ex.Message}");
            CatalogStatus.Text = CatalogText("Не удалось удалить базу.", "Could not remove the database.");
        }
        RefreshCatalogRows();
    }
}
