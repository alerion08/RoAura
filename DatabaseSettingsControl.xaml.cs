using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseSettingsControl : UserControl
{
    private readonly GameDatabaseService _database = (Application.Current as App)?.MarketWatchDatabase ?? new();
    private readonly System.Windows.Threading.DispatcherTimer _previewTimer = new();
    public DatabaseSettingsControl()
    {
        InitializeComponent();
        WatchEditor.Initialize(_database);
        _previewTimer.Tick += (_, _) => { PreviewBorder.Visibility = Visibility.Collapsed; _previewTimer.Stop(); };
        Unloaded += (_, _) => _previewTimer.Stop();
    }
    private App? CurrentApp => Application.Current as App;
    private void OpenDatabase_Click(object sender, RoutedEventArgs e) => CurrentApp?.ToggleDatabase();
    private void WatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WatchEditor is null) return;
        var rule = WatchList.SelectedItem as MarketWatchRule;
        EditWatchButton.IsEnabled = DeleteWatchButton.IsEnabled = rule is not null;
        var item = rule is null ? null : _database.Item(rule.ItemId) ?? new DatabaseItem { Id = rule.ItemId, Name = rule.ItemName };
        WatchEditor.Initialize(_database, item, rule, allowItemChange: true);
    }
    private void AddWatch_Click(object sender, RoutedEventArgs e)
    {
        if (WatchEditor.CreateRule() is not { } added) return;
        CurrentApp?.SaveMarketWatchRule(added);
        WatchList.SelectedItem = added;
    }
    private void EditWatch_Click(object sender, RoutedEventArgs e)
    {
        if (WatchList.SelectedItem is not MarketWatchRule existing || WatchEditor.CreateRule() is not { } updated) return;
        CurrentApp?.SaveMarketWatchRule(updated, existing.ItemId);
        WatchList.SelectedItem = updated;
    }
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
    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AppSettings settings) return;
        var config = settings.MarketWatchNotifications;
        PreviewText.Text = Services.MarketWatchService.FormatText(config.TextTemplate, new(730, "1carat Diamond", 5000));
        PreviewIcon.Source = string.IsNullOrWhiteSpace(config.IconImageBase64)
            ? new System.Windows.Media.Imaging.BitmapImage(new Uri("https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/items/730.png"))
            : new Converters.Base64ToImageSourceConverter().Convert(config.IconImageBase64, typeof(ImageSource), null!, System.Globalization.CultureInfo.CurrentCulture) as ImageSource;
        PreviewBorder.Visibility = Visibility.Visible;
        _previewTimer.Stop();
        _previewTimer.Interval = TimeSpan.FromSeconds(config.DurationSeconds);
        _previewTimer.Start();
        CurrentApp?.PreviewMarketWatch();
    }
    private void Settings_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CurrentApp?.PersistMarketWatchSettings();
    private void Color_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || DataContext is not AppSettings settings) return;
        var fallback = box == BackgroundEditor ? "#DD102B50" : "#FFFFFFFF";
        try { if (ColorConverter.ConvertFromString(box.Text) is not Color) throw new FormatException(); }
        catch { box.Text = fallback; box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); }
    }
}
