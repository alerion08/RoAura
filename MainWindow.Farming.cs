using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MainWindow
{
    private MvpTimerWizardWindow? _gameMvpWizard;
    internal bool IsMvpWizardOpen => _gameMvpWizard?.IsVisible == true;

    internal void AddMvpTimerInGame()
    {
        if (_gameMvpWizard is not null || !FeatureAvailability.CanUse(Settings.ServerProfile,
                Settings.PacketCaptureEnabled, AppFeature.MvpTimers) ||
            !ForegroundProcess.TryGetForegroundAllowedBounds(Settings.AllowedProcesses, out var bounds)) return;
        var game = QuestWindowNative.GetForegroundWindow();
        var server = Settings.ServerProfile;
        var wizard = new MvpTimerWizardWindow(_monsters)
        {
            StartAfterCreate = true, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Topmost = true
        };
        AppearanceService.ApplyPalette(wizard, AppearanceService.Resolve(Settings.ApplicationTheme, Settings.GameOverlayTheme, true));
        _gameMvpWizard = wizard;
        var follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        void Place()
        {
            if (!QuestWindowNative.IsWindow(game) || Settings.ServerProfile != server ||
                !ForegroundProcess.TryFindWindow(Settings.AllowedProcesses, out var current, out var rectangle) || current != game)
            { wizard.Close(); return; }
            var width = Math.Min(900, rectangle.Right - rectangle.Left);
            var height = Math.Min(760, rectangle.Bottom - rectangle.Top);
            var dpi = QuestWindowNative.Scale(new WindowInteropHelper(wizard).Handle);
            wizard.MinWidth = Math.Min(680, width / dpi); wizard.MinHeight = Math.Min(480, height / dpi);
            QuestWindowNative.Place(wizard, new Rect(rectangle.Left + (rectangle.Right-rectangle.Left-width)/2,
                rectangle.Top + (rectangle.Bottom-rectangle.Top-height)/2, width, height));
        }
        follow.Tick += (_, _) => Place();
        wizard.SourceInitialized += (_, _) => { QuestWindowNative.Configure(wizard, false, false); Place(); };
        wizard.Loaded += (_, _) =>
        {
            follow.Start();
            (Application.Current as App)?.RefreshKeyboardMode();
        };
        try
        {
            if (wizard.ShowDialog() == true && wizard.CreatedTimer is { } timer && Settings.ServerProfile == server)
            {
                timer.SetMonsterDetails(_monsters.FirstOrDefault(monster => monster.Id == timer.MonsterId));
                Settings.ActiveMvpTimers.Add(timer); SaveSettings();
            }
        }
        finally
        {
            follow.Stop(); _gameMvpWizard = null;
            (Application.Current as App)?.RefreshKeyboardMode();
            if (QuestWindowNative.IsWindow(game)) QuestWindowNative.SetForegroundWindow(game);
        }
    }
}
