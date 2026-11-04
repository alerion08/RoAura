using System.Windows;
using System.Windows.Input;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class FarmingFeedbackTests
{
    [Theory]
    [InlineData("F4",0x73,ModifierKeys.None)]
    [InlineData("Control+Shift+A",0x41,ModifierKeys.Control|ModifierKeys.Shift)]
    [InlineData("Alt+1",0x31,ModifierKeys.Alt)]
    [InlineData("Win+Shift+S",0x53,ModifierKeys.Windows|ModifierKeys.Shift)]
    [InlineData("Esc",0x1B,ModifierKeys.None)]
    public void Chords_parse_legacy_keys_and_modifier_combinations(string text,int key,ModifierKeys modifiers)
    {
        Assert.True(HotkeyChord.TryParse(text,out var chord));
        Assert.Equal(key,chord.VirtualKey);Assert.Equal(modifiers,chord.Modifiers);
        Assert.True(chord.Matches(key,modifiers));
        Assert.True(HotkeyChord.TryParse(chord.ToString(),out var roundtrip));
        Assert.Equal(chord,roundtrip);
    }
    [Fact]
    public void Capture_release_gate_and_exact_modifiers_prevent_unintended_actions()
    {
        Assert.True(HotkeyChord.TryParse("Ctrl+F4",out var chord));
        Assert.False(chord.Matches(0x73,ModifierKeys.None));
        Assert.False(chord.Matches(0x73,ModifierKeys.Control|ModifierKeys.Shift));
        HotkeyCaptureGate.IsCapturing=true;
        try
        {
            Assert.True(HotkeyCaptureGate.Suppressed(_=>false));
            HotkeyCaptureGate.IsCapturing=false;HotkeyCaptureGate.AwaitingReleaseKey=0x73;
            Assert.True(HotkeyCaptureGate.Suppressed(key=>key==0x73));
            Assert.False(HotkeyCaptureGate.Suppressed(_=>false));
        }
        finally{HotkeyCaptureGate.IsCapturing=false;HotkeyCaptureGate.AwaitingReleaseKey=0;}
    }
    [Fact]
    public void Removing_character_does_not_reappear_on_discovery_or_broaden_scopes_or_presets()
    {
        var settings=new AppSettings();
        var key=CharacterStatisticsKey.Create("SpringRO",123);
        CharacterScopeService.Discover(settings,[key]);
        var profile=Assert.Single(settings.Characters);
        var old=new AppSettings();old.Characters.Add(profile);
        var scope=new CharacterScope();scope.Characters.Add(profile.Id);
        Assert.True(CharacterScopeService.Remove(settings,profile));
        Assert.False(CharacterScopeService.Discover(settings,[key]));
        SettingsSnapshot.Apply(old,settings);
        Assert.Empty(settings.Characters);
        Assert.Single(scope.Characters);
        Assert.Contains(CharacterScopeService.IdentityKey("SPRINGRO",123),settings.RemovedCharacterKeys);
        Assert.Equal(HotkeyChord.Canonical("Ctrl+A"),HotkeyChord.Canonical("Control+a"));
        settings.SettingsHotkey="Ctrl+A";settings.OverlaysHotkey="Control+a";
        Assert.True(settings.HotkeyConflicts);
    }
    [Theory]
    [InlineData(700,40,200,60)]
    [InlineData(10,40,200,60)]
    [InlineData(400,650,200,60)]
    public void Map_preview_never_overlaps_timer_panel(double x,double y,double width,double height)
    {
        var panel=new Rect(x,y,width,height);var client=new Rect(0,0,1000,800);
        var placement=MvpMapPlacement.Find(panel,new Size(280,305),client);
        Assert.False(placement.IsEmpty);Assert.True(client.Contains(placement));
        Assert.False(placement.IntersectsWith(panel));
    }
}
