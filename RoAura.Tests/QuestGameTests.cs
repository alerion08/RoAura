using System.Reflection;
using RoAura.Models;
using RoAura.Services;
using Xunit;
namespace RoAura.Tests;

public sealed class QuestGameTests
{
    [Theory]
    [InlineData("Talk to Serin (niflheim 313 70).", "niflheim",313,70)]
    [InlineData("Enter the building (niflheim 187 241) then visit Crayu (nif_in 105 81).", "niflheim",187,241)]
    [InlineData("Visit ra_in01 372 200.", "ra_in01",372,200)]
    [InlineData("Go to niflheim 313,70.", "niflheim",313,70)]
    public void QualifiedWikiCoordinatesSupportBothSeparators(string text,string map,int x,int y)
    {
        var maps=new HashSet<string>(["niflheim","nif_in"],StringComparer.OrdinalIgnoreCase);
        Assert.Contains(QuestWikiLocations.Parse(text,maps),p=>p.Map==map && p.X==x && p.Y==y);
    }
    [Theory]
    [InlineData("Buy item_count 10 20.")]
    [InlineData("Collect 10 Jellopy and 20 Red Potion.")]
    [InlineData("Base Level 60 and Job Level 30.")]
    [InlineData("niflheim 3130 70")]
    [InlineData("niflheim 313 7000")]
    [InlineData("313 70")]
    public void WikiCoordinateSupplementDoesNotGuessMapsFromNumericProse(string text)=>
        Assert.Empty(QuestWikiLocations.Parse(text,new HashSet<string>(["niflheim"],StringComparer.OrdinalIgnoreCase)));
    [Fact]
    public void FactsKeepSourceLabelsValuesUnknownsAndNumberSeparators()
    {
        var facts=QuestFactFormatter.Parse("Base Level: 60 Quest Corequisite(s): Steps 1 through 5 of Rachel Sanctuary Quest Item(s) (Consumed): 7 Milk , 5 Cacao");
        Assert.Equal(3,facts.Count);Assert.Equal("60",facts[0].Value);
        Assert.Equal("Steps 1 through 5 of Rachel Sanctuary Quest",facts[1].Value);
        Assert.Equal(new[]{"7 Milk","5 Cacao"},QuestFactFormatter.Lines(facts[2]));
        var rewards=QuestFactFormatter.Parse("Base Experience: 900,000 Item(s): 1 Old Purple Box");
        Assert.Equal("900,000",rewards[0].Value);Assert.Equal("1 Old Purple Box",rewards[1].Value);
        Assert.Single(QuestFactFormatter.Parse("Unknown server-specific text"));Assert.Empty(QuestFactFormatter.Parse(null));
    }
    [Fact]
    public void VariableRewardsStaySourceTextInsteadOfInventedTotals()
    {
        var facts=QuestFactFormatter.Parse("Base Experience: Level 50-75 = 400,000 EXP Level 75-90 = 800,000 EXP [EXP is given in two parts] Item(s): Pass , Knife");
        Assert.Contains("[EXP is given in two parts]",facts[0].Value);
        Assert.Equal(2,QuestFactFormatter.Lines(facts[0]).Count());
        Assert.Equal(new[]{"Pass","Knife"},QuestFactFormatter.Lines(facts[1]));
        var price=QuestFactFormatter.Parse("Item(s): 1,000 Zeny , Box");
        Assert.Equal(new[]{"1,000 Zeny","Box"},QuestFactFormatter.Lines(price[0]));
    }
    private static QuestListRow Row(params QuestLocation[] locations)
    {
        var guide=QuestProgressTests.Guide() with {Steps=[new(1,1,"numbered",null,"Original instruction",locations)]};
        return new(new QuestProgressEntry {QuestKey=guide.Key,CurrentStep=1},guide);
    }
    [Fact]
    public void NavigationRejectsBareCandidateAndInjectedMapsButRetainsAllDistinctWikiTargets()
    {
        var row=Row(new("rachel",100,240,"map_coord","literal","wiki_literal_not_npc_verified"),
            new("ra_in01",384,246,"map_coord","literal","wiki_literal_not_npc_verified"),
            new("rachel",100,240,"map_coord","repeat","wiki_literal_not_npc_verified"),
            new(null,10,0,"bare_coord","10:0","wiki_literal_not_npc_verified"),
            new("rachel",1,2,"map_coord","candidate","candidate"),
            new("map\n/quit",1,2,"map_coord","bad","wiki_literal_not_npc_verified"));
        var targets=QuestNavigation.Targets(row);Assert.Equal(2,targets.Count);
        Assert.Contains(targets,t=>t.Command=="/navi rachel 100/240");
        Assert.Contains(targets,t=>t.Command=="/navi ra_in01 384/246");
        row.Progress.NeedsStepReview=true;Assert.Empty(QuestNavigation.Targets(row));
    }
    [Theory]
    [InlineData(-1,5)][InlineData(5,-1)][InlineData(1024,5)][InlineData(5,1024)]
    public void NavigationDoesNotGuessOutOfRangeCells(int x,int y)=>Assert.Empty(QuestNavigation.Targets(Row(new QuestLocation("rachel",x,y,"map_coord","literal","wiki_literal_not_npc_verified"))));
    [Fact]
    public void NewHotkeyConflictsDisableOnlyNewBindingsAndRetainLegacyKeys()
    {
        var settings=new AppSettings();Assert.Equal((true,true),QuestHotkeys.Enabled(settings));
        settings.DatabaseHotkey="F6";Assert.Equal((false,true),QuestHotkeys.Enabled(settings));
        using var hook=new GlobalInputHook(settings);
        Assert.Equal(0,Field(hook,"_questBrowserKey"));Assert.Equal(0x75,Field(hook,"_databaseKey"));
        settings.QuestBrowserHotkey="F4";Assert.Equal(0x73,Field(hook,"_questBrowserKey"));
        settings.QuestTrackerHotkey="F4";Assert.Equal((false,false),QuestHotkeys.Enabled(settings));
        Assert.Equal("F6",settings.DatabaseHotkey);
    }
    [Theory]
    [InlineData(0x10)][InlineData(0x11)][InlineData(0x12)][InlineData(0x5B)][InlineData(0x5C)]
    public void ModifiersRemainPassThroughEvenInInteractiveMode(int key)
    {
        var method=typeof(GlobalInputHook).GetMethod("IsModifier",BindingFlags.NonPublic|BindingFlags.Static)!;
        Assert.True((bool)method.Invoke(null,[key])!);
    }
    [Fact]
    public void BrowserGeometryAndKeysParticipateInPresetsWithoutChangingOtherOverlayCoordinates()
    {
        var source=new AppSettings {QuestBrowserHotkey="F3",QuestTrackerHotkey="F4"};
        source.QuestOverlay.BrowserX=123;source.QuestOverlay.BrowserY=77;source.QuestOverlay.BrowserWidth=1050;
        source.Skills.Add(new SkillAura {X=333,Y=444});
        var target=new AppSettings();var options=target.QuestOverlay;
        SettingsSnapshot.Apply(source,target);
        Assert.Same(options,target.QuestOverlay);Assert.Equal(123,options.BrowserX);Assert.Equal(1050,options.BrowserWidth);
        Assert.Equal("F3",target.QuestBrowserHotkey);Assert.Equal("F4",target.QuestTrackerHotkey);
        Assert.Equal(333,target.Skills.Single().X);Assert.Equal(444,target.Skills.Single().Y);
    }
    private static int Field(GlobalInputHook hook,string name)=>(int)typeof(GlobalInputHook).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(hook)!;
}
