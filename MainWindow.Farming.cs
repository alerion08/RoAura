using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MainWindow
{
    private void StatisticsTutorial_Click(object sender, RoutedEventArgs e) =>
        new EffectTutorialWindow("StatisticsSettings.mp4", "StatisticsTutorialTitle") { Owner = this }.ShowDialog();

    private void GoldGoblinTutorial_Click(object sender, RoutedEventArgs e) =>
        new EffectTutorialWindow("GoldGoblinOverview.mp4", "GoldGoblinTutorialTitle") { Owner = this }.ShowDialog();

    private MvpTimerWizardWindow? _gameMvpWizard;
    internal void CloseMvpWizard() => _gameMvpWizard?.Close();
    internal bool IsMvpWizardOpen => _gameMvpWizard?.IsVisible == true;

    internal void AddMvpTimerInGame()
    {
        if (_gameMvpWizard is not null) { _gameMvpWizard.Close(); return; }
        if (!FeatureAvailability.CanUse(Settings.ServerProfile,
                Settings.PacketCaptureEnabled, AppFeature.MvpTimers) ||
            !ForegroundProcess.TryGetForegroundAllowedBounds(Settings.AllowedProcesses, out var bounds)) return;
        var game = QuestWindowNative.GetForegroundWindow();
        var server = Settings.ServerProfile;
        var wizard = new MvpTimerWizardWindow(_monsters)
        {
            Tag = "RoAura.GameInterface", StartAfterCreate = true, WindowStyle = WindowStyle.None,
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
