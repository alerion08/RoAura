using System.Windows;
using RoAura.Services;

namespace RoAura;

public partial class MainWindow
{
    private OverlayLayoutEditor? _layoutEditor;
    internal OverlayLayoutEditor? InterfaceLayoutEditor => _layoutEditor;
    internal bool IsInterfaceLayoutEditing => _layoutEditor?.IsActive == true;
    private void InitializeInterfaceLayout()
    {
        _layoutEditor = new(Settings, _overlay, _questWindows, () => SaveSettings(),
            enabled => (Application.Current as App)?.SetEscapeHotkeyEnabled(enabled));
        Activated += (_, _) => _layoutEditor.Stop();
    }
    internal void StopInterfaceLayout() => _layoutEditor?.Stop();
    internal void ToggleInterfaceLayout()
    {
        if (IsInterfaceLayoutEditing) { StopInterfaceLayout(); return; }
        if (!ForegroundProcess.IsForegroundAllowed(Settings.AllowedProcesses)) return;
        EndOverlayEditModes();
        HideQuestBrowser(); CloseMvpWizard();
        (Application.Current as App)?.CloseDatabaseOverlay();
        Hide();
        _layoutEditor?.Start();
    }
}
