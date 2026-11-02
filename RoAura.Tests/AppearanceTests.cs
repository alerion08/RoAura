using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Models;
using RoAura.Services;
using Xunit;
namespace RoAura.Tests;
public sealed class AppearanceTests
{
 [Theory]
 [InlineData("Dark","Light","Dark","Light")]
 [InlineData("Light","Dark","Light","Dark")]
 [InlineData("Dark","Application","Dark","Dark")]
 [InlineData("Light","Application","Light","Light")]
 public void ApplicationAndGameScopesResolveIndependently(string app,string game,string expectedApp,string expectedGame)
 {
  Assert.Equal(expectedApp,AppearanceService.Resolve(app,game,false));
  Assert.Equal(expectedGame,AppearanceService.Resolve(app,game,true));
 }
 [Fact] public void OldAndInvalidSettingsKeepLightDefaults()
 {
  var settings=new AppSettings();Assert.Equal("Light",settings.ApplicationTheme);Assert.Equal("Application",settings.GameOverlayTheme);
  settings.ApplicationTheme="invalid";settings.GameOverlayTheme="invalid";
  Assert.Equal("Light",settings.ApplicationTheme);Assert.Equal("Application",settings.GameOverlayTheme);
 }
 [Fact] public void PersistenceAndPresetsKeepThemeChoicesAndUserColors()
 {
  var root=Path.Combine(Path.GetTempPath(),"RoAura.Appearance",Guid.NewGuid().ToString("N"));
  try
  {
   var store=new SettingsStore(root);var settings=store.Load();settings.ApplicationTheme="Dark";settings.GameOverlayTheme="Light";
   settings.TimersTextColor="#FF123456";settings.QuestOverlay.TextColor="#FFAABBCC";settings.QuestOverlay.X=111;settings.QuestOverlay.OpacityPercent=63;
   store.Save(settings);var restored=store.Load();var target=new AppSettings();SettingsSnapshot.Apply(restored,target);
   Assert.Equal("Dark",target.ApplicationTheme);Assert.Equal("Light",target.GameOverlayTheme);
   Assert.Equal("#FF123456",target.TimersTextColor);Assert.Equal("#FFAABBCC",target.QuestOverlay.TextColor);Assert.Equal(111,target.QuestOverlay.X);Assert.Equal(63,target.QuestOverlay.OpacityPercent);
  }
  finally {if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 [Fact] public void LocalPalettesSwitchWithoutChangingOtherScopes()
 {
  Exception? error=null;var thread=new Thread(()=>
  {
   try
   {
    var app=new Border();var game=new Border();AppearanceService.ApplyPalette(app,"Dark");AppearanceService.ApplyPalette(game,"Light");
    Assert.Equal(Color.FromRgb(20,27,39),((SolidColorBrush)app.FindResource("RoWindowBrush")).Color);
    Assert.Equal(Color.FromRgb(234,243,251),((SolidColorBrush)game.FindResource("RoWindowBrush")).Color);
    AppearanceService.ApplyPalette(game,"Dark");AppearanceService.ApplyPalette(app,"Light");
    Assert.Equal(Color.FromRgb(20,27,39),((SolidColorBrush)game.FindResource("RoWindowBrush")).Color);
    Assert.Equal(Color.FromRgb(234,243,251),((SolidColorBrush)app.FindResource("RoWindowBrush")).Color);
    var light=new ResourceDictionary{Source=new Uri("/RoAura;component/Themes/LightPalette.xaml",UriKind.Relative)};
    var dark=new ResourceDictionary{Source=new Uri("/RoAura;component/Themes/DarkPalette.xaml",UriKind.Relative)};
    foreach(var key in light.Keys)Assert.True(dark.Contains(key),"Missing dark token: "+key);
   }catch(Exception ex){error=ex;}
  });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)ExceptionDispatchInfo.Capture(error).Throw();
 }
}
