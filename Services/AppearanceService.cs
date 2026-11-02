using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using RoAura.Models;
namespace RoAura.Services;

/// <summary>Window-scoped palettes. Theme changes never rewrite user overlay colors.</summary>
public static class AppearanceService
{
 private static AppSettings? _settings;
 private static bool _registered;
 public static event Action? Changed;
 public static string NormalizeApplication(string? value)=>value=="Dark"?"Dark":"Light";
 public static string NormalizeOverlays(string? value)=>value is "Dark" or "Light"?value:"Application";
 public static string Resolve(string? application,string? overlays,bool game)=>game && NormalizeOverlays(overlays)!="Application"?NormalizeOverlays(overlays):NormalizeApplication(application);
 public static void Initialize(AppSettings settings)
 {
  if(_settings is not null)_settings.PropertyChanged-=SettingsChanged;
  _settings=settings;_settings.PropertyChanged+=SettingsChanged;
  if(!_registered)
  {
   EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler(WindowLoaded));
   EventManager.RegisterClassHandler(typeof(ToolTip),ToolTip.OpenedEvent,new RoutedEventHandler(PopupOpened));
   EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.OpenedEvent,new RoutedEventHandler(PopupOpened));_registered=true;
  }
  Refresh();
 }
 private static void PopupOpened(object sender,RoutedEventArgs e)
 {
  var target=sender switch{ToolTip tip=>tip.PlacementTarget as FrameworkElement,ContextMenu menu=>menu.PlacementTarget as FrameworkElement,_=>null};
  if(sender is FrameworkElement popup && target is not null)
   ApplyPalette(popup,target.TryFindResource("AppearancePaletteName") as string??Resolve(_settings?.ApplicationTheme,_settings?.GameOverlayTheme,IsGameWindow(Window.GetWindow(target))));
 }
 private static void WindowLoaded(object sender,RoutedEventArgs e)
 {
  if(!ReferenceEquals(sender,e.OriginalSource) || sender is not Window window)return;
  ApplyWindow(window);
 }
 private static void SettingsChanged(object? sender,PropertyChangedEventArgs e)
 {if(e.PropertyName is nameof(AppSettings.ApplicationTheme) or nameof(AppSettings.GameOverlayTheme) or "")Refresh();}
 public static bool IsGameWindow(Window? window)
 {
  for(var n=0;n<12 && window is not null;n++,window=window.Owner)
   if(window is DatabaseOverlayWindow or DatabaseDetailWindow or QuestBrowserWindow or QuestPassiveWindow)return true;
  return false;
 }
 public static void Refresh()
 {
  if(Application.Current is {} app)
   foreach(Window window in app.Windows)ApplyWindow(window);
  Changed?.Invoke();
 }
 public static void ApplyWindow(Window window)
 {
  // Ordinary transparent HUD surfaces retain all configured data colors.
  if(window is OverlayWindow)return;
  ApplyPalette(window,Resolve(_settings?.ApplicationTheme,_settings?.GameOverlayTheme,IsGameWindow(window)));
 }
 public static void ApplyPalette(FrameworkElement root,string theme)
 {
  var name=NormalizeApplication(theme);
  if(root.Resources["AppearancePaletteName"] as string==name)return;
  var old=root.Resources.MergedDictionaries.Where(d=>d.Source?.OriginalString.Contains("Palette.xaml")==true).ToArray();
  var palette=new ResourceDictionary{Source=new Uri($"/RoAura;component/Themes/{name}Palette.xaml",UriKind.Relative)};
  foreach(var dictionary in old)root.Resources.MergedDictionaries.Remove(dictionary);
  root.Resources.MergedDictionaries.Add(palette);root.Resources["AppearancePaletteName"]=name;
  // Native defaults used by list and menu controls must match this scope too.
  root.Resources[SystemColors.WindowBrushKey]=root.FindResource("RoCardBrush");
  root.Resources[SystemColors.WindowTextBrushKey]=root.FindResource("RoTextBrush");
  root.Resources[SystemColors.ControlBrushKey]=root.FindResource("RoPanelBrush");
  root.Resources[SystemColors.ControlTextBrushKey]=root.FindResource("RoTextBrush");
  root.Resources[SystemColors.HighlightBrushKey]=root.FindResource("RoSelectionBrush");
  root.Resources[SystemColors.HighlightTextBrushKey]=root.FindResource("RoTextBrush");
 }
 public static Brush Brush(FrameworkElement owner,string color,string role="Foreground")
 {
  var token="Ui"+role+"_"+color.TrimStart('#').ToUpperInvariant();
  return owner.TryFindResource(token) as Brush??(Brush)new BrushConverter().ConvertFromString(color)!;
 }
}
