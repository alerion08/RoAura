using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class QuestInteractionTests
{
    [Fact]
    public async Task HistoricalEventsAreExcludedWithoutErasingSavedProgress()
    {
        var normal=QuestProgressTests.Guide("normal");
        var old=QuestProgressTests.Guide("event") with {Category="historical_or_seasonal"};
        var catalog=new QuestCatalog([normal,old]);Assert.Single(catalog.Guides);Assert.Contains("event",catalog.ArchivedKeys);
        Assert.Empty(catalog.Search("event"));
        var root=Path.Combine(Path.GetTempPath(),"RoAura-Archived-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new QuestProgressStore(root);var key=new QuestProfileKey("SpringRO",123,null);var session=store.Load(key);
            QuestProgress.Add(session.Document,old);store.Save(session);
            var workspace=new QuestWorkspace(new AppSettings {ServerProfile="SpringRO",PacketCaptureEnabled=false},store,catalog:catalog);
            await workspace.SelectProfileAsync(key,default);Assert.Empty(workspace.Active);Assert.Empty(workspace.History);
            Assert.Single(store.Load(key).Document.Quests);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]
    public async Task CompletionMarksAndCollapsePersistAndIdenticalInstructionsStayIndependent()
    {
        var root=Path.Combine(Path.GetTempPath(),"RoAura-Marks-"+Guid.NewGuid().ToString("N"));
        try
        {
            var guide=QuestProgressTests.Guide("repeated","v1","Same","Same","Last");
            var settings=new AppSettings {ServerProfile="SpringRO",PacketCaptureEnabled=false};
            var catalog=new QuestCatalog([guide]);var store=new QuestProgressStore(root);
            var workspace=new QuestWorkspace(settings,store,catalog:catalog);var key=new QuestProfileKey("SpringRO",123,null);
            await workspace.SelectProfileAsync(key,default);workspace.ToggleTracking(guide);
            workspace.SetStepDone(guide,guide.Steps[0],true);
            Assert.True(workspace.IsStepDone(guide,guide.Steps[0]));Assert.False(workspace.IsStepDone(guide,guide.Steps[1]));
            Assert.Equal(2,workspace.Active.Single().Progress.CurrentStep);
            workspace.ToggleCollapsed(workspace.Active.Single());Assert.True(await workspace.FlushAsync());
            var next=new QuestWorkspace(settings,store,catalog:catalog);await next.SelectProfileAsync(key,default);
            Assert.True(next.Active.Single().Progress.IsCollapsed);Assert.True(next.IsStepDone(guide,guide.Steps[0]));
            next.SetStepDone(guide,guide.Steps[0],false);Assert.False(next.IsStepDone(guide,guide.Steps[0]));
            Assert.Equal(2,next.Active.Single().Progress.CurrentStep);
            await next.SelectProfileAsync(new("SpringRO",456,null),default);Assert.False(next.IsStepDone(guide,guide.Steps[0]));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]
    public void InstructionHighlightsPreserveSourceAndPreferAnswerOverItem()
    {
        const string source="Type \"Milk\" then bring 3 Milk and a Red Potion. Milky stays plain.";
        var parts=QuestHighlights.Split(source,["Milk"],["Milk","Red Potion"]);
        Assert.Equal(source,string.Concat(parts.Select(p=>p.Text)));
        Assert.Equal("item",Assert.Single(parts.Where(p=>p.Text=="Red Potion")).Kind);
        Assert.All(parts.Where(p=>p.Text=="Milk"),p=>Assert.Equal("answer",p.Kind));
        Assert.Contains(parts,p=>p.Kind=="" && p.Text.Contains("Milky"));
        Assert.Empty(QuestHighlights.Split("",[],[]));
    }
    [Fact]
    public void TrackerLimitSurvivesSettingsPresetAndClamps()
    {
        var source=new AppSettings();Assert.Equal(5,source.QuestOverlay.MaxTrackedQuests);
        source.QuestOverlay.MaxTrackedQuests=0;Assert.Equal(1,source.QuestOverlay.MaxTrackedQuests);
        source.QuestOverlay.MaxTrackedQuests=99;Assert.Equal(5,source.QuestOverlay.MaxTrackedQuests);
        source.QuestOverlay.MaxTrackedQuests=4;
        var copy=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(source))!;
        var target=new AppSettings();SettingsSnapshot.Apply(copy,target);Assert.Equal(4,target.QuestOverlay.MaxTrackedQuests);
    }
    [Fact]
    public void PartialBrowserOverlapClipsOnlyCoveredActionPixels()
    {
        var clip=OverlayOcclusion.CreateClip(new System.Windows.Size(320,24),[new System.Windows.Rect(290,0,760,500)]);
        Assert.NotNull(clip);
        Assert.True(clip.FillContains(new System.Windows.Point(20,12)));
        Assert.True(clip.FillContains(new System.Windows.Point(200,12)));
        Assert.False(clip.FillContains(new System.Windows.Point(310,12)));
    }
    [Theory]
    [InlineData("Talk to her and type \"Freya's Spring\".", "Freya's Spring")]
    [InlineData("Type in Yellow Key .", "Yellow Key")]
    [InlineData("Examine it and type in Black Key. You'll get an Oval.", "Black Key")]
    [InlineData("Type in ‘unknown’", null)]
    [InlineData("Type in your password to enter the room.", null)]
    [InlineData("Choose \"No thanks\" and enter the house.", null)]
    [InlineData("Enter \"Hotel\" and speak to the owner.", null)]
    public void AnswerCopyUsesOnlyExplicitLiteralInput(string source,string? expected)
    {
        var values=QuestAnswers.Extract(source);
        if(expected is null)Assert.Empty(values);else Assert.Equal(expected,Assert.Single(values));
    }
    [Fact]
    public void InstructionLinesClampAndSurvivePresetAndSerialization()
    {
        var settings=new AppSettings();Assert.Equal(3,settings.QuestOverlay.DescriptionLines);
        settings.QuestOverlay.DescriptionLines=100;Assert.Equal(20,settings.QuestOverlay.DescriptionLines);
        settings.QuestOverlay.DescriptionLines=0;Assert.Equal(1,settings.QuestOverlay.DescriptionLines);
        settings.QuestOverlay.DescriptionLines=6;
        var copy=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        var target=new AppSettings();SettingsSnapshot.Apply(copy,target);Assert.Equal(6,target.QuestOverlay.DescriptionLines);
    }
    [Fact]
    public void DoneAdvancesThenCompletesExactlyOnceWhileNavigationStaysManual()
    {
        var guide=QuestProgressTests.Guide();
        var entry=QuestProgress.Add(QuestProgressDocument.Create(new("SpringRO",123,null)),guide);
        Assert.True(QuestProgress.ConfirmStep(entry,guide,1,DateTime.UtcNow));
        Assert.Equal(2,entry.CurrentStep);Assert.Empty(entry.History);
        Assert.False(QuestProgress.ConfirmStep(entry,guide,1,DateTime.UtcNow)); // stale/double click
        Assert.Equal(2,entry.CurrentStep);
        Assert.True(QuestProgress.Move(entry,guide,1));Assert.Equal(QuestManualState.Active,entry.State);
        Assert.True(QuestProgress.ConfirmStep(entry,guide,3,DateTime.UtcNow));
        Assert.Equal(QuestManualState.Completed,entry.State);
        Assert.Equal("manual",Assert.Single(entry.History).Method);
        Assert.False(QuestProgress.ConfirmStep(entry,guide,3,DateTime.UtcNow));Assert.Single(entry.History);
    }
    [Fact]
    public void DoneRejectsUnreviewedMissingAndInactiveInstructions()
    {
        var guide=QuestProgressTests.Guide();var entry=new QuestProgressEntry {QuestKey=guide.Key,CurrentStep=1,NeedsStepReview=true};
        Assert.False(QuestProgress.ConfirmStep(entry,guide,1,DateTime.UtcNow));
        entry.NeedsStepReview=false;entry.IsMissingFromCatalog=true;
        Assert.False(QuestProgress.ConfirmStep(entry,guide,1,DateTime.UtcNow));
        entry.IsMissingFromCatalog=false;entry.State=QuestManualState.Paused;
        Assert.False(QuestProgress.ConfirmStep(entry,guide,1,DateTime.UtcNow));
        Assert.Throws<ArgumentException>(()=>QuestProgress.ConfirmStep(entry,QuestProgressTests.Guide("other"),1,DateTime.UtcNow));
    }
    [Fact]
    public void LinksPreserveLiteralTextResolveLongestTitlesAndRejectAmbiguity()
    {
        var a=QuestProgressTests.Guide("a") with {Title="Sanctuary Quest"};
        var b=QuestProgressTests.Guide("b") with {Title="Rachel Sanctuary Quest"};
        var text="Steps 1 through 5 of Rachel Sanctuary Quest; Sanctuary Quest. Unknown Quest";
        var parts=QuestLinks.Split(text,new QuestCatalog([a,b]));
        Assert.Equal(text,string.Concat(parts.Select(p=>p.Text)));
        Assert.Equal(new[]{"b","a"},parts.Where(p=>p.Guide is not null).Select(p=>p.Guide!.Key));
        Assert.All(QuestLinks.Split("Rachel Sanctuary Quest",new QuestCatalog([b,b with {Key="duplicate"}])),p=>Assert.Null(p.Guide));
        Assert.All(QuestLinks.Split("Sanctuary QuestExtra",new QuestCatalog([a])),p=>Assert.Null(p.Guide));
    }
    [Fact]
    public async Task StarsAndDoneRetainProgressPerProfileAcrossRestart()
    {
        var root=Path.Combine(Path.GetTempPath(),"RoAura-QuestInteraction-"+Guid.NewGuid().ToString("N"));
        try
        {
            var guide=QuestProgressTests.Guide();var catalog=new QuestCatalog([guide]);var settings=new AppSettings {ServerProfile="SpringRO",PacketCaptureEnabled=false};
            var store=new QuestProgressStore(root);var workspace=new QuestWorkspace(settings,store,catalog:catalog);
            workspace.ToggleTracking(guide);Assert.Empty(workspace.Active);
            var profile=new QuestProfileKey("SpringRO",123,null);
            await workspace.SelectProfileAsync(profile,default);
            workspace.ToggleTracking(guide);var row=Assert.Single(workspace.Active);
            workspace.Done(row,1);workspace.ToggleTracking(guide);Assert.Empty(workspace.Active);
            workspace.ToggleTracking(guide);Assert.Equal(2,Assert.Single(workspace.Active).Progress.CurrentStep);
            workspace.Done(row,2);workspace.Done(row,3);Assert.Empty(workspace.Active);
            Assert.Equal(QuestManualState.Completed,Assert.Single(workspace.History).Progress.State);
            Assert.True(await workspace.FlushAsync());
            var next=new QuestWorkspace(settings,store,catalog:catalog);await next.SelectProfileAsync(profile,default);
            Assert.Empty(next.Active);Assert.Equal(QuestManualState.Completed,Assert.Single(next.History).Progress.State);
            await next.SelectProfileAsync(new("SpringRO",456,null),default);Assert.Empty(next.History);
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
