using System.Diagnostics;
using System.Windows;
using RoAura.Services;

namespace RoAura;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _update;
    private readonly GitHubUpdateService _service;
    private readonly bool _english;

    public UpdateWindow(UpdateInfo update, GitHubUpdateService service, bool english)
    {
        InitializeComponent();
        _update = update;
        _service = service;
        _english = english;
        if (english)
        {
            Title = "RoAura update";
            TitleText.Text = "Update available";
            DescriptionText.Text = "RoAura can download and install the update automatically. Your settings and presets will be preserved.";
            LaterButton.Content = "Later";
            InstallButton.Content = "Update";
        }
        else DescriptionText.Text = "RoAura может скачать и установить обновление автоматически. Настройки и пресеты будут сохранены.";
        VersionText.Text = $"{GitHubUpdateServiceVersion()}  →  {_update.Version}";
        NotesText.Text = string.IsNullOrWhiteSpace(_update.Notes)
            ? (english ? "No release notes were provided." : "Описание изменений пока не добавлено.")
            : _update.Notes;
    }

    private string GitHubUpdateServiceVersion() => _service.CurrentVersion.ToString(3);

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        StatusText.Text = _english ? "Downloading update…" : "Загрузка обновления…";
        var progress = new Progress<double>(value => DownloadProgress.Value = value);
        try
        {
            var installer = await _service.DownloadInstallerAsync(_update, progress);
            StatusText.Text = _english ? "Starting installer…" : "Запуск установщика…";
            GitHubUpdateService.StartInstaller(installer);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UPDATE INSTALL ERROR: {ex}");
            StatusText.Text = _english ? "Automatic update failed." : "Не удалось установить обновление автоматически.";
            InstallButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            if (MessageBox.Show(this,
                    (_english ? "Open the release page in your browser?\n\n" : "Открыть страницу релиза в браузере?\n\n") + ex.Message,
                    "RoAura", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(_update.ReleaseUrl) { UseShellExecute = true });
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();
}
