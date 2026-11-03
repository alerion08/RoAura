using RoAura.Models;
using RoAura.Services;
using Xunit;
namespace RoAura.Tests;

public sealed class MvpFormRefinementTests
{
    [Fact]
    public void Default_location_is_first_available_and_does_not_replace_explicit_choice()
    {
        var timer=new MvpTimer();
        timer.SetMonsterDetails(new MonsterEntry {Id=1,Spawns=[new(){Code="first"},new(){Code="second"}]});
        timer.SelectFirstLocationIfMissing();Assert.Equal("first",timer.LocationCode);
        timer.LocationCode="second";timer.MarkerX=.3;timer.MarkerY=.7;
        timer.SelectFirstLocationIfMissing();Assert.Equal("second",timer.LocationCode);Assert.Equal(.3,timer.MarkerX);
    }
    [Fact]
    public void Explicitly_unassigned_hotkeys_remain_unassigned_through_snapshots()
    {
        var source=new AppSettings {SettingsHotkey="None",AddMvpTimerHotkey="None",FinishMoveHotkey="None"};
        var target=new AppSettings();SettingsSnapshot.Apply(source,target);
        Assert.Equal("None",target.SettingsHotkey);Assert.Equal("None",target.AddMvpTimerHotkey);
        Assert.Equal("None",target.FinishMoveHotkey);Assert.False(HotkeyChord.TryParse(target.SettingsHotkey,out _));
        source.MvpAddWindowX=123;source.MvpAddWindowY=45;SettingsSnapshot.Apply(source,target);
        Assert.Equal(123,target.MvpAddWindowX);Assert.Equal(45,target.MvpAddWindowY);
    }
}
