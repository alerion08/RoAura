using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class QuestWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoAura-QuestWorkspace-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new() { ServerProfile = "SpringRO", PacketCaptureEnabled = false };
    private static CharacterSessionSnapshot Observed(uint? cid = null) => new("SpringRO", cid, cid is null ? CharacterSessionPhase.Unknown : CharacterSessionPhase.ActiveObserved, 1, 2);
    private QuestWorkspace Workspace(QuestProgressStore? store = null) => new(_settings, store ?? new(_root),
        catalog: new QuestCatalog([QuestProgressTests.Guide(), QuestProgressTests.Guide("friendship")]));
    [Fact]
    public async Task OfflineRequiresExplicitChoiceAndRetainsIndependentProgressAcrossRestart()
    {
        var workspace = Workspace(); await workspace.RefreshIdentityAsync(Observed());
        Assert.False(workspace.CanEdit);
        workspace.SelectedGuide = workspace.Catalog!.Guides["lost"]; workspace.AddSelected(); Assert.Empty(workspace.Active);
        var key = new QuestProfileKey("SpringRO", null, QuestWorkspace.LocalProfile);
        await workspace.SelectProfileAsync(key, Observed());
        workspace.AddSelected(); workspace.Move(workspace.Active.Single(), 1);
        workspace.SelectedGuide = workspace.Catalog.Guides["friendship"]; workspace.AddSelected();
        Assert.True(await workspace.FlushAsync()); Assert.False(workspace.HasPendingWrites);
        var next = Workspace(); await next.SelectProfileAsync(key, Observed());
        Assert.Equal(2, next.Active.Single(q => q.Progress.QuestKey == "lost").Progress.CurrentStep);
        Assert.Equal(1, next.Active.Single(q => q.Progress.QuestKey == "friendship").Progress.CurrentStep);
    }
    [Fact]
    public async Task ObservedLogoutAndRapidProfileSwitchesNeverLeakRows()
    {
        _settings.PacketCaptureEnabled = true;
        var workspace = Workspace(); await workspace.RefreshIdentityAsync(Observed(1));
        workspace.SelectedGuide = workspace.Catalog!.Guides["lost"]; workspace.AddSelected();
        var first = workspace.RefreshIdentityAsync(Observed(2));
        var second = workspace.RefreshIdentityAsync(Observed(3));
        await Task.WhenAll(first, second); Assert.Empty(workspace.Active);
        await workspace.RefreshIdentityAsync(Observed()); Assert.False(workspace.CanEdit); Assert.Empty(workspace.Active);
        await workspace.RefreshIdentityAsync(Observed(1)); Assert.Single(workspace.Active);
    }
    [Fact]
    public async Task ServerSwitchClearsManualChoiceAndPresetsRetainProgress()
    {
        var workspace = Workspace(); var key = new QuestProfileKey("SpringRO", null, QuestWorkspace.LocalProfile);
        await workspace.SelectProfileAsync(key, Observed()); workspace.SelectedGuide = workspace.Catalog!.Guides["lost"]; workspace.AddSelected();
        var preset = new AppSettings { ServerProfile = "SpringRO", PacketCaptureEnabled = false };
        preset.QuestOverlay.Width = 440; preset.QuestOverlay.X = 123;
        var liveVisual = _settings.QuestOverlay;
        SettingsSnapshot.Apply(preset, _settings);
        Assert.Same(liveVisual, _settings.QuestOverlay); Assert.Equal(440, liveVisual.Width); Assert.Single(workspace.Active);
        _settings.ServerProfile = "ToC"; await workspace.RefreshIdentityAsync(Observed());
        Assert.Null(workspace.ManualProfile); Assert.Empty(workspace.Active); Assert.False(workspace.CanEdit);
        _settings.ServerProfile = "SpringRO"; await workspace.SelectProfileAsync(key, Observed()); Assert.Single(workspace.Active);
    }
    [Fact]
    public async Task HistoryRestoreReorderAndVisibilityRetainOtherQuestPosition()
    {
        var workspace = Workspace(); await workspace.SelectProfileAsync(new("SpringRO", null, QuestWorkspace.LocalProfile), Observed());
        workspace.SelectedGuide = workspace.Catalog!.Guides["lost"]; workspace.AddSelected();
        workspace.SelectedGuide = workspace.Catalog.Guides["friendship"]; workspace.AddSelected();
        var row = workspace.Active.Last(); workspace.Move(row, 1); workspace.SetVisibility(row, false); workspace.Reorder(row, -1);
        Assert.Equal("friendship", workspace.Active.First().Progress.QuestKey);
        workspace.SetState(workspace.Active.First(), QuestManualState.Paused); Assert.Single(workspace.History);
        workspace.SetState(workspace.History.Single(), QuestManualState.Active);
        Assert.Equal(2, workspace.Active.First().Progress.CurrentStep); Assert.False(workspace.Active.First().Progress.IsVisibleInOverlay);
        Assert.Equal(1, workspace.Active.Last().Progress.CurrentStep);
        Assert.True(await workspace.FlushAsync());
    }
    [Fact]
    public async Task FailedSaveBlocksScopeSwitchAndRetainsUnsavedDocumentForRetry()
    {
        var store = new QuestProgressStore(_root); var workspace = Workspace(store);
        var key = new QuestProfileKey("SpringRO", null, QuestWorkspace.LocalProfile);
        await workspace.SelectProfileAsync(key, Observed()); workspace.SelectedGuide = workspace.Catalog!.Guides["lost"]; workspace.AddSelected();
        var stale = store.Load(key); store.Save(stale); // conflicting external writer
        Assert.False(await workspace.FlushAsync()); Assert.True(workspace.HasPendingWrites); Assert.NotEmpty(workspace.Error);
        await workspace.SelectProfileAsync(new("SpringRO", 99, null), Observed()); Assert.False(workspace.CanEdit); Assert.True(workspace.HasPendingWrites);
        File.Delete(store.PathFor(key)); // remove test conflict, not production recovery behavior
        Assert.True(await workspace.SaveAndRefreshAsync(Observed())); Assert.True(workspace.CanEdit); Assert.Empty(workspace.Active);
        await workspace.SelectProfileAsync(key, Observed()); Assert.Single(workspace.Active);
    }
    [Fact]
    public async Task ReloadAfterMissingCatalogCanSucceedAndCancelIsSafeAfterCompletion()
    {
        var workspace = new QuestWorkspace(_settings, new(_root), new(Path.Combine(_root, "missing.sqlite")));
        await workspace.LoadCatalogAsync(); Assert.NotEmpty(workspace.CatalogError); Assert.False(workspace.IsLoadingCatalog);
        await workspace.LoadCatalogAsync(); Assert.NotEmpty(workspace.CatalogError);
        workspace.CancelCatalogLoad(); // no disposed CTS crash
    }
    [Theory]
    [InlineData("RTM")][InlineData("SpringRO")][InlineData("ToC")][InlineData("Other")]
    public void QuestsRemainAvailableWithoutPacketsAndExistingEnumValuesStayStable(string server)
    {
        Assert.True(FeatureAvailability.CanUse(server, false, AppFeature.Quests));
        Assert.Equal(8, (int)AppFeature.Database); Assert.Equal(9, (int)AppFeature.Settings); Assert.Equal(10, (int)AppFeature.Quests);
    }
    [Fact]
    public void Schema13UpgradeRetainsCoordinatesAndAddsVisualDefaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "settings.json"), """{"SettingsVersion":13,"OverlayCoordinatesRelativeToGame":true,"Skills":[{"X":321,"Y":654}],"QuestOverlay":null}""");
        var settings = new SettingsStore(_root).Load(); Assert.Equal(14, settings.SettingsVersion);
        Assert.Equal(321, settings.Skills.Single().X); Assert.Equal(654, settings.Skills.Single().Y);
        Assert.Equal(12, settings.QuestOverlay.X); Assert.Equal(200, settings.QuestOverlay.Y);
        Assert.Contains(settings.NavigationSections, p => p.Feature == AppFeature.Quests);
    }
    [Fact]
    public async Task OfflineChoicesIncludeSavedCidButNeverMoveHistoryWithAnAlias()
    {
        var store = new QuestProgressStore(_root);
        var existing = store.Load(new("SpringRO", 77, null)); store.Save(existing); store.Save(existing);
        File.Delete(store.PathFor(new("SpringRO", 77, null))); // backup-only record remains selectable
        _settings.Characters.Add(new CharacterProfile { CharacterId = 88, DetectedServer = "ToC", ServerProfile = "SpringRO", Name = "Alias" });
        var workspace = Workspace(store);
        Assert.Contains(workspace.ProfileChoices(), p => p.Key?.CharacterId == 77);
        Assert.DoesNotContain(workspace.ProfileChoices(), p => p.Key?.CharacterId == 88);
        await workspace.SelectProfileAsync(new("SpringRO", 77, null), Observed());
        Assert.False(workspace.CanEdit); Assert.NotEmpty(workspace.Error);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
