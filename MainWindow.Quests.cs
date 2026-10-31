using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using RoAura.Services;
using RoAura.Models;

namespace RoAura;

public partial class MainWindow
{
    private QuestWorkspace _questWorkspace = null!;
    private readonly DispatcherTimer _questSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _questClosing, _questCloseFlushed;
    private QuestOverlayHost _questWindows=null!;
    private bool _questTargetBlocked;
    private long _questTargetRevisionFloor;
    public bool IsQuestBrowserOpen=>_questWindows?.BrowserRequested==true && _questWindows.BrowserShowing;
    private CharacterSessionSnapshot CurrentQuestSession()=>_questTargetBlocked
        ? new("",null,CharacterSessionPhase.Unknown,0,0) : _packetCapture.CharacterSession;
    private void InitializeQuests()
    {
        _questWorkspace = new QuestWorkspace(Settings);
        QuestPage.Initialize(_questWorkspace, CurrentQuestSession, RequestSave);
        _questWindows=new(_questWorkspace,this,CurrentQuestSession,()=>_overlay.IsBusyEditing,RequestSave,
            enabled=>(Application.Current as App)?.SetEscapeHotkeyEnabled(enabled));
        _questWindows.KeyboardModeChanged+=()=> (Application.Current as App)?.RefreshKeyboardMode();
        _questWindows.TargetChanged+=(previous,current)=>
        {
            if(previous==IntPtr.Zero || previous==current)return;
            _questTargetBlocked=true;_questTargetRevisionFloor=_packetCapture.CharacterSession.Revision;
            _=_questWorkspace.RefreshIdentityAsync(CurrentQuestSession());
        };
        QuestPage.OpenBrowserRequested+=ToggleQuestBrowser;
        QuestPage.ToggleTrackerRequested+=ToggleQuestTracker;
        QuestPage.MoveTrackerRequested+=()=> {if(_questWindows.PrepareMove()){_overlay.SetOverlaysVisible(true);HideFromHotkey();}else QuestPage.Report(LocalizationService.Get("QNoGame"));};
        _questWorkspace.SaveRequested += RequestQuestSave;
        _questSaveTimer.Tick += QuestSave_Tick;
        _packetCapture.CharacterSessionChanged += QuestSessionChanged;
        Settings.PropertyChanged += QuestSettingsChanged;
        _ = _questWorkspace.RefreshIdentityAsync(_packetCapture.CharacterSession);
        if(Settings.QuestOverlay.AutoLoadCatalog)_ = _questWorkspace.LoadCatalogAsync();
    }
    private void RequestQuestSave() { _questSaveTimer.Stop(); _questSaveTimer.Start(); }
    private async void QuestSave_Tick(object? sender, EventArgs e)
    {
        _questSaveTimer.Stop();
        if (await _questWorkspace.FlushAsync() && _questWorkspace.HasPendingWrites) RequestQuestSave();
    }
    private void QuestSessionChanged(CharacterSessionSnapshot snapshot) => Dispatcher.BeginInvoke(async () =>
    {
        var current = _packetCapture.CharacterSession;
        if (snapshot.Revision == current.Revision && snapshot.Phase == current.Phase)
        {
            if(_questTargetBlocked && current.Revision>_questTargetRevisionFloor && current.Phase==CharacterSessionPhase.ActiveObserved)
                _questTargetBlocked=false;
            await _questWorkspace.RefreshIdentityAsync(CurrentQuestSession());
        }
    });
    private async void QuestSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.ServerProfile) or nameof(AppSettings.PacketCaptureEnabled) or "")
            await _questWorkspace.RefreshIdentityAsync(CurrentQuestSession());
        if (e.PropertyName is nameof(AppSettings.Language) or "") _questWorkspace.RefreshLanguage();
    }
    private async Task FlushQuestsThenCloseAsync()
    {
        _questClosing = true;
        try
        {
            if (!await _questWorkspace.FlushAsync())
            {
                MessageBox.Show(this, _questWorkspace.Error, "ROQuestie", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_questWorkspace.HasPendingWrites) return; // user changed a row while the save was in flight
            _questCloseFlushed = true; Close();
        }
        finally { _questClosing = false; }
    }
    public void ToggleQuestBrowser()
    {
        if(!QuestToggleContext())return;
        _questWindows.ToggleBrowser();
        if(_questWindows.GameHandle==IntPtr.Zero)QuestPage.Report(LocalizationService.Get("QNoGame"));
    }
    public void ToggleQuestTracker() {if(QuestToggleContext())_questWindows.ToggleTracker();}
    private bool QuestToggleContext()=>IsActive || IsQuestBrowserOpen || ForegroundProcess.IsForegroundAllowed(Settings.AllowedProcesses);
    public bool TryFinishQuestDrag() {if(!_questWindows.IsDragging)return false;_questWindows.EndDrag();return true;}
    public void HideQuestBrowser(bool restoreFocus=false)=>_questWindows.HideBrowser(restoreFocus);
    public void SetQuestGlobalVisible(bool visible)=>_questWindows.SetGlobalVisible(visible);
    private void CloseQuests()
    {
        _questSaveTimer.Stop(); _questWorkspace.CancelCatalogLoad();_questWindows.Dispose();
        _questWorkspace.SaveRequested -= RequestQuestSave;
        _packetCapture.CharacterSessionChanged -= QuestSessionChanged;
        Settings.PropertyChanged -= QuestSettingsChanged;
        QuestPage.Detach();
    }
}
