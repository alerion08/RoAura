using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RoAura.Models;

namespace RoAura;

public partial class DatabaseSettingsControl : UserControl
{
    public DatabaseSettingsControl() => InitializeComponent();
    private App? CurrentApp => Application.Current as App;
    private void OpenDatabase_Click(object sender, RoutedEventArgs e) => CurrentApp?.ToggleDatabase();
    private void AddWatch_Click(object sender, RoutedEventArgs e) => CurrentApp?.EditMarketWatch(Window.GetWindow(this));
    private void EditWatch_Click(object sender, RoutedEventArgs e)
    { if (WatchList.SelectedItem is MarketWatchRule rule) CurrentApp?.EditMarketWatch(Window.GetWindow(this), rule); }
    private void DeleteWatch_Click(object sender, RoutedEventArgs e)
    {
        if (WatchList.SelectedItem is not MarketWatchRule rule || DataContext is not AppSettings settings) return;
        settings.MarketWatchRules.Remove(rule);
        CurrentApp?.PersistMarketWatchSettings();
    }
    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AppSettings settings) return;
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            using var stream = new MemoryStream(bytes);
            _ = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            settings.MarketWatchNotifications.IconImageBase64 = Convert.ToBase64String(bytes);
            CurrentApp?.PersistMarketWatchSettings();
        }
        catch (Exception ex) { Services.DebugLog.Write($"MARKET WATCH ICON ERROR: {ex}"); }
    }
    private void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppSettings settings) settings.MarketWatchNotifications.IconImageBase64 = "";
        CurrentApp?.PersistMarketWatchSettings();
    }
    private void ChooseSound_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AppSettings settings) return;
        var dialog = new OpenFileDialog { Filter = "Audio|*.wav;*.mp3;*.wma;*.m4a" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        settings.MarketWatchNotifications.SoundPath = dialog.FileName;
        settings.MarketWatchNotifications.SoundEnabled = true;
        CurrentApp?.PersistMarketWatchSettings();
    }
    private void Preview_Click(object sender, RoutedEventArgs e) => CurrentApp?.PreviewMarketWatch();
    private void Settings_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CurrentApp?.PersistMarketWatchSettings();
    private void Color_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || DataContext is not AppSettings settings) return;
        var fallback = box == BackgroundEditor ? "#DD102B50" : "#FFFFFFFF";
        try { if (ColorConverter.ConvertFromString(box.Text) is not Color) throw new FormatException(); }
        catch { box.Text = fallback; box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); }
    }
}
