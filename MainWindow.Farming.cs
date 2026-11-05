using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MainWindow
{
    private MvpTimerWizardWindow? _gameMvpWizard;
    internal void CloseMvpWizard() => _gameMvpWizard?.Close();
    internal bool IsMvpWizardOpen => _gameMvpWizard?.IsVisible == true;

    private bool UsesReferenceTimerCatalog =>
        Settings.ServerProfile is GameServerProfile.MuhRo or GameServerProfile.Other;

    internal void AddMvpTimerInGame()
    {
        if (_gameMvpWizard is not null) { _gameMvpWizard.Close(); return; }
        OpenMvpTimerInGame();
    }

    internal void RestartMvpTimerInGame(int number, DateTime killedAtUtc)
    {
        var target = MvpTimerHotkeys.Resolve(Settings, number);
        if (target is null) return;
        if (_gameMvpWizard is { } current)
        {
            var same = current.IsRestart && current.Draft.Id == target.Id;
            var server = Settings.ServerProfile;
            current.Close();
            if (!same) Dispatcher.BeginInvoke(() =>
            {
                if (Settings.ServerProfile == server && Settings.ActiveMvpTimers.Contains(target))
                    OpenMvpTimerInGame(target, killedAtUtc);
            });
            return;
        }
        OpenMvpTimerInGame(target, killedAtUtc);
    }

    private void OpenMvpTimerInGame(MvpTimer? restartSource = null, DateTime? killedAtUtc = null)
    {
        if (_gameMvpWizard is not null || (restartSource is not null && !Settings.ActiveMvpTimers.Contains(restartSource))) return;
        if (!FeatureAvailability.CanUse(Settings.ServerProfile,
                Settings.PacketCaptureEnabled, AppFeature.MvpTimers) ||
            !ForegroundProcess.TryGetForegroundAllowedBounds(Settings.AllowedProcesses, out var bounds)) return;
        var game = QuestWindowNative.GetForegroundWindow();
        var server = Settings.ServerProfile;
        var timers = Settings.ActiveMvpTimers;
        var wizard = new MvpTimerWizardWindow(_monsters, restartSource, killedAtUtc, UsesReferenceTimerCatalog)
        {
            Tag = "RoAura.GameInterface", StartAfterCreate = restartSource is null, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Topmost = true,
            Background = System.Windows.Media.Brushes.Transparent
        };
        AppearanceService.ApplyPalette(wizard, AppearanceService.Resolve(Settings.ApplicationTheme, Settings.GameOverlayTheme, true));
        _gameMvpWizard = wizard;
        var follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        void Place()
        {
            if (!QuestWindowNative.IsWindow(game) || Settings.ServerProfile != server ||
                !ReferenceEquals(Settings.ActiveMvpTimers, timers) || (restartSource is not null && !timers.Contains(restartSource)) ||
                !ForegroundProcess.TryFindWindow(Settings.AllowedProcesses, out var current, out var rectangle) || current != game)
            { wizard.Close(); return; }
            if (wizard.IsDragging) return;
            var gameDpi = QuestWindowNative.Scale(game);
            var width = Math.Min(800 * gameDpi, rectangle.Right - rectangle.Left);
            var height = Math.Min(540 * gameDpi, rectangle.Bottom - rectangle.Top);
            var dpi = QuestWindowNative.Scale(new WindowInteropHelper(wizard).Handle);
            wizard.MinWidth = Math.Min(580, width / dpi); wizard.MinHeight = Math.Min(360, height / dpi);
            var x=Settings.MvpAddWindowX is double savedX && double.IsFinite(savedX) ? rectangle.Left+savedX*gameDpi : rectangle.Left+(rectangle.Right-rectangle.Left-width)/2;
            var y=Settings.MvpAddWindowY is double savedY && double.IsFinite(savedY) ? rectangle.Top+savedY*gameDpi : rectangle.Top+(rectangle.Bottom-rectangle.Top-height)/2;
            QuestWindowNative.Place(wizard,new Rect(
                Math.Clamp(x,rectangle.Left,Math.Max(rectangle.Left,rectangle.Right-width)),
                Math.Clamp(y,rectangle.Top,Math.Max(rectangle.Top,rectangle.Bottom-height)),width,height));
        }
        wizard.PositionChangedByUser += () =>
        {
            if (!ForegroundProcess.TryFindWindow(Settings.AllowedProcesses,out var target,out var rect) || target!=game) return;
            var position=QuestWindowNative.Bounds(new WindowInteropHelper(wizard).Handle);
            var dpi=QuestWindowNative.Scale(game);
            Settings.MvpAddWindowX=(position.Left-rect.Left)/dpi;
            Settings.MvpAddWindowY=(position.Top-rect.Top)/dpi;
            _store.Save(Settings);
            Place();
        };
        follow.Tick += (_, _) => Place();
        wizard.SourceInitialized += (_, _) => { QuestWindowNative.Configure(wizard, false, false); Place(); };
        wizard.Loaded += (_, _) =>
        {
            follow.Start();
            (Application.Current as App)?.RefreshKeyboardMode();
        };
        try
        {
            if (wizard.ShowDialog() == true && wizard.CreatedTimer is { } timer && Settings.ServerProfile == server &&
                ReferenceEquals(Settings.ActiveMvpTimers, timers))
            {
                if (restartSource is not null)
                {
                    if (!timers.Contains(restartSource)) return;
                    restartSource.ApplyRestartDraft(timer);
                }
                else timers.Add(timer);
                MvpTimerHotkeys.RefreshNumbers(timers);
                SaveSettings();
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
