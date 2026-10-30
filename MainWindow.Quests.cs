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
    private void InitializeQuests()
    {
        _questWorkspace = new QuestWorkspace(Settings);
        QuestPage.Initialize(_questWorkspace, () => _packetCapture.CharacterSession, RequestSave);
        _questWorkspace.SaveRequested += RequestQuestSave;
        _questSaveTimer.Tick += QuestSave_Tick;
        _packetCapture.CharacterSessionChanged += QuestSessionChanged;
        Settings.PropertyChanged += QuestSettingsChanged;
        _ = _questWorkspace.RefreshIdentityAsync(_packetCapture.CharacterSession);
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
            await _questWorkspace.RefreshIdentityAsync(current);
    });
    private async void QuestSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.ServerProfile) or nameof(AppSettings.PacketCaptureEnabled) or "")
            await _questWorkspace.RefreshIdentityAsync(_packetCapture.CharacterSession);
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
    private void CloseQuests()
    {
        _questSaveTimer.Stop(); _questWorkspace.CancelCatalogLoad();
        _questWorkspace.SaveRequested -= RequestQuestSave;
        _packetCapture.CharacterSessionChanged -= QuestSessionChanged;
        Settings.PropertyChanged -= QuestSettingsChanged;
        QuestPage.Detach();
    }
}
