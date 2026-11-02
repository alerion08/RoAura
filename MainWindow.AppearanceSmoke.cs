using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoAura.Services;
namespace RoAura;
public partial class MainWindow
{
 internal void ValidateAppearanceSmoke()
 {
  var app=Settings.ApplicationTheme;var game=Settings.GameOverlayTheme;var text=Settings.QuestOverlay.TextColor;var timers=Settings.TimersTextColor;
  var output=Environment.GetEnvironmentVariable("ROAURA_QUEST_UI_SNAPSHOTS");
  try
  {
   foreach(var (a,g) in new[]{("Dark","Light"),("Light","Dark"),("Dark","Application"),("Light","Application")})
   {
    Settings.ApplicationTheme=a;Settings.GameOverlayTheme=g;AppearanceService.Refresh();
    if(Resources["AppearancePaletteName"] as string!=a || _questWindows.Browser.Resources["AppearancePaletteName"] as string!=AppearanceService.Resolve(a,g,true))
     throw new InvalidOperationException("Independent appearance scopes did not update.");
    if(!string.IsNullOrWhiteSpace(output) && a=="Dark")
    {
     var page=new SettingsPage{DataContext=Settings};AppearanceService.ApplyPalette(page,a);page.ShowAppearancePreview();
     page.Measure(new Size(1040,660));page.Arrange(new Rect(0,0,1040,660));page.UpdateLayout();
     var bitmap=new RenderTargetBitmap(1040,660,96,96,PixelFormats.Pbgra32);bitmap.Render(page);
     var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(output);
     using var file=File.Create(Path.Combine(output,"appearance-"+a+"-"+g+".png"));png.Save(file);
    }
   }
   if(Settings.QuestOverlay.TextColor!=text || Settings.TimersTextColor!=timers)throw new InvalidOperationException("Appearance changed custom HUD colors.");
  }
  finally{Settings.ApplicationTheme=app;Settings.GameOverlayTheme=game;AppearanceService.Refresh();}
 }
}
