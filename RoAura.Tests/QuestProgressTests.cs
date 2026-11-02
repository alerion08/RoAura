using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public class QuestProgressTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoAura-QuestTest-" + Guid.NewGuid().ToString("N"));
    private static readonly QuestProfileKey Character = new("SpringRO", 123, null);
    internal static QuestGuide Guide(string key = "lost", string hash = "v1", params string[] instructions) => new(
        key, key, "https://example.test/quest", "walkthrough_parsed", "candidate", null, null, null, null,
        hash, "fresh", null, null, null, null,
        (instructions.Length == 0 ? new[] { "First", "Second", "Last" } : instructions)
            .Select((text, i) => new QuestStep(i + 1, i + 1, "numbered", null, text, [])).ToArray());

    [Fact]
    public void TwoQuestsMoveIndependentlyAndLastStepDoesNotComplete()
    {
        var doc = QuestProgressDocument.Create(Character);
        var a = Guide(); var b = Guide("friendship");
        var first = QuestProgress.Add(doc, a); var second = QuestProgress.Add(doc, b);
        Assert.Same(first, QuestProgress.Add(doc, a));
        Assert.False(QuestProgress.Move(first, a, -1));
        Assert.True(QuestProgress.Move(first, a, 1));
        Assert.Equal(1, second.CurrentStep);
        Assert.True(QuestProgress.Move(first, a, 1));
        Assert.False(QuestProgress.Move(first, a, 1));
        Assert.Equal(QuestManualState.Active, first.State);
        Assert.True(QuestProgress.Move(first, a, -1));
        Assert.Equal(2, first.CurrentStep);
        Assert.Throws<ArgumentException>(() => QuestProgress.Move(first, b, 1));
    }

    [Fact]
    public void RestartRetainsPositionVisibilityOrderAndManualHistory()
    {
        var store = new QuestProgressStore(_root);
        var session = store.Load(Character);
        var entry = QuestProgress.Add(session.Document, Guide());
        QuestProgress.SelectStep(entry, Guide(), 3);
        entry.Order = 7; entry.IsVisibleInOverlay = false;
        Assert.Throws<InvalidOperationException>(() => QuestProgress.SetState(entry, QuestManualState.Completed, DateTime.UtcNow));
        QuestProgress.SetState(entry, QuestManualState.Completed, DateTime.UtcNow, true);
        QuestProgress.SetState(entry, QuestManualState.Active, DateTime.UtcNow);
        store.Save(session);
        var loaded = new QuestProgressStore(_root).Load(Character).Document.Quests.Single();
        Assert.Equal(3, loaded.CurrentStep); Assert.Equal(7, loaded.Order); Assert.False(loaded.IsVisibleInOverlay);
        Assert.Equal(new[] { QuestManualState.Completed, QuestManualState.Active }, loaded.History.Select(h => h.State));
        Assert.All(loaded.History, h => Assert.Equal("manual", h.Method));
    }

    [Fact]
    public void CatalogRefreshMovesUniqueAnchorButDoesNotGuessChangedOrDuplicateText()
    {
        var doc = QuestProgressDocument.Create(Character);
        var entry = QuestProgress.Add(doc, Guide());
        QuestProgress.SelectStep(entry, Guide(), 2);
        QuestProgress.Reconcile(doc, new QuestCatalog([Guide("lost", "v2", "Inserted", "First", "Second", "Last")]));
        Assert.Equal(3, entry.CurrentStep); Assert.False(entry.NeedsStepReview);
        Assert.Equal("v2", entry.CatalogContentHash);
        QuestProgress.Reconcile(doc, new QuestCatalog([Guide("lost", "v3", "Second", "Second")]));
        Assert.True(entry.NeedsStepReview); Assert.Equal(3, entry.CurrentStep);
        Assert.False(QuestProgress.Move(entry, Guide(), -1));
        QuestProgress.Reconcile(doc, new QuestCatalog([Guide("lost", "v4", "Different")]));
        Assert.True(entry.NeedsStepReview); Assert.Equal(3, entry.CurrentStep);
        QuestProgress.SelectStep(entry, Guide("lost", "v4", "Different"), 1);
        Assert.False(entry.NeedsStepReview);
    }

    [Fact]
    public void MissingQuestAndHistorySurviveSaveAndReturn()
    {
        var store = new QuestProgressStore(_root); var session = store.Load(Character);
        var entry = QuestProgress.Add(session.Document, Guide());
        QuestProgress.SetState(entry, QuestManualState.Removed, DateTime.UtcNow);
        QuestProgress.Reconcile(session.Document, new QuestCatalog([]));
        store.Save(session);
        var doc = store.Load(Character).Document;
        Assert.True(doc.Quests.Single().IsMissingFromCatalog);
        QuestProgress.Reconcile(doc, new QuestCatalog([Guide()]));
        Assert.False(doc.Quests.Single().IsMissingFromCatalog);
        Assert.Equal(QuestManualState.Removed, doc.Quests.Single().State);
        Assert.Single(doc.Quests.Single().History);
    }

    [Fact]
    public void UnchangedRepeatedInstructionsRetainTheirExplicitPosition()
    {
        var guide = Guide("repeat", "same", "Talk", "Talk");
        var doc = QuestProgressDocument.Create(Character);
        var entry = QuestProgress.Add(doc, guide);
        QuestProgress.SelectStep(entry, guide, 2);
        QuestProgress.Reconcile(doc, new QuestCatalog([guide]));
        Assert.False(entry.NeedsStepReview); Assert.Equal(2, entry.CurrentStep);
        QuestProgress.Reconcile(doc, new QuestCatalog([Guide("repeat", "new", "Talk", "Talk")]));
        Assert.True(entry.NeedsStepReview); Assert.Equal(2, entry.CurrentStep);
    }

    [Fact]
    public void ServerCidAndExplicitLocalProfilesAreIsolated()
    {
        var keys = new[] { Character, new QuestProfileKey("ToC", 123, null), new QuestProfileKey("SpringRO", 456, null),
            new QuestProfileKey("SpringRO", null, Guid.NewGuid()) };
        var store = new QuestProgressStore(_root);
        foreach (var (key, i) in keys.Select((k, i) => (k, i)))
        { var s = store.Load(key); QuestProgress.Add(s.Document, Guide("quest" + i)); store.Save(s); }
        foreach (var (key, i) in keys.Select((k, i) => (k, i)))
            Assert.Equal("quest" + i, store.Load(key).Document.Quests.Single().QuestKey);
        Assert.Throws<ArgumentException>(() => new QuestProfileKey("../bad", 1, null));
        Assert.Throws<ArgumentException>(() => new QuestProfileKey("SpringRO", null, null));
        Assert.Throws<ArgumentException>(() => new QuestProfileKey("SpringRO", 1, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(CharacterSessionPhase.Unknown, true, "SpringRO")]
    [InlineData(CharacterSessionPhase.Connecting, true, "SpringRO")]
    [InlineData(CharacterSessionPhase.Suspended, true, "SpringRO")]
    [InlineData(CharacterSessionPhase.ActiveObserved, false, "SpringRO")]
    [InlineData(CharacterSessionPhase.ActiveObserved, true, "ToC")]
    public void UnknownLogoutPacketsOffAndServerSwitchNeverReuseCid(CharacterSessionPhase phase, bool packets, string server)
    {
        var snapshot = new CharacterSessionSnapshot("SpringRO", 123, phase, 1, 2, 3, 999);
        Assert.Null(QuestProfileKey.FromObserved(snapshot, server, packets));
    }

    [Fact]
    public void ObservedIdentityUsesCidNeverAid()
    {
        Assert.Equal(Character, QuestProfileKey.FromObserved(new("SpringRO", 123, CharacterSessionPhase.ActiveObserved, 1, 2, 3, 999), "SpringRO", true));
        Assert.Null(QuestProfileKey.FromObserved(new("SpringRO", null, CharacterSessionPhase.ActiveObserved, 1, 2, 3, 999), "SpringRO", true));
    }

    [Theory]
    [InlineData("{bad json")]
    [InlineData("{\"SchemaVersion\":99}")]
    [InlineData("{\"SchemaVersion\":1,\"Server\":\"SPRINGRO\",\"CharacterId\":123,\"Quests\":null}")]
    public void CorruptOrFuturePrimaryUsesReadOnlyBackupAndPreservesBothFiles(string corrupt)
    {
        var store = new QuestProgressStore(_root); var session = store.Load(Character);
        QuestProgress.Add(session.Document, Guide()); store.Save(session); store.Save(session);
        var path = store.PathFor(Character); var backup = File.ReadAllBytes(path + ".bak");
        File.WriteAllText(path, corrupt);
        var recovered = store.Load(Character);
        Assert.True(recovered.IsReadOnly); Assert.NotNull(recovered.RecoveryWarning);
        Assert.Single(recovered.Document.Quests);
        Assert.Throws<InvalidDataException>(() => store.Save(recovered));
        Assert.Equal(corrupt, File.ReadAllText(path)); Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
    }

    [Fact]
    public void BackupOnlyCannotBeOverwrittenAndMissingSchemaIsRejected()
    {
        var store = new QuestProgressStore(_root); var session = store.Load(Character);
        store.Save(session); store.Save(session);
        var path = store.PathFor(Character); File.Delete(path);
        Assert.True(store.Load(Character).IsReadOnly);
        Assert.Throws<IOException>(() => store.Save(session));
        File.Delete(path + ".bak"); File.WriteAllText(path, "{}");
        Assert.Throws<InvalidDataException>(() => store.Load(Character));
    }

    [Fact]
    public void StaleWriterAndCrossIdentitySaveCannotOverwriteData()
    {
        var store = new QuestProgressStore(_root); var first = store.Load(Character); var stale = store.Load(Character);
        QuestProgress.Add(first.Document, Guide()); store.Save(first);
        Assert.Throws<IOException>(() => store.Save(stale));
        first.Document.CharacterId = 456;
        Assert.Throws<InvalidDataException>(() => store.Save(first));
        Assert.Single(store.Load(Character).Document.Quests);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.PathFor(Character))!, "*.tmp"));
    }

    [Fact]
    public void DuplicateAndInvalidEntriesAreRejectedBeforeAnyWrite()
    {
        var store = new QuestProgressStore(_root); var session = store.Load(Character);
        var entry = QuestProgress.Add(session.Document, Guide()); session.Document.Quests.Add(entry);
        Assert.Throws<InvalidDataException>(() => store.Save(session));
        Assert.False(File.Exists(store.PathFor(Character)));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
