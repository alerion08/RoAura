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
    private MarketWatchNotificationSettings? _previewSettings;
    private ImageSource? _previewIcon;
    private string? _previewIconKey;
    public static readonly DependencyProperty PreviewNotificationProperty = DependencyProperty.Register(nameof(PreviewNotification), typeof(MarketWatchNotification), typeof(DatabaseSettingsControl));
    public MarketWatchNotification? PreviewNotification { get => (MarketWatchNotification?)GetValue(PreviewNotificationProperty); private set => SetValue(PreviewNotificationProperty, value); }
    public DatabaseSettingsControl()
    {
        InitializeComponent();
        WatchEditor.Initialize(_database);
        Loaded += (_, _) =>
        {
            AttachPreview();
            if (CurrentApp is { } app) { app.MarketWatchHistoryChanged -= RefreshHistory; app.MarketWatchHistoryChanged += RefreshHistory; }
            RefreshHistory();
        };
        DataContextChanged += (_, _) => AttachPreview();
        Unloaded += (_, _) => { if (CurrentApp is { } app) app.MarketWatchHistoryChanged -= RefreshHistory; if (_previewSettings is not null) _previewSettings.PropertyChanged -= PreviewSettingsChanged; _previewSettings = null; };
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
        RefreshHistory();
    }
    private void RefreshHistory()
    {
        if (HistoryGrid is null) return;
        var selected = WatchList.SelectedItem as MarketWatchRule;
        HistoryItemText.Text = selected?.ItemName ?? "";
        var rows = (CurrentApp?.MarketWatchHistory ?? Array.Empty<MarketWatchHistoryEntry>())
            .Where(entry => selected is null || entry.ItemId == selected.ItemId)
            .OrderByDescending(entry => entry.DetectedAtUtc).ToList();
        HistoryGrid.ItemsSource = rows;
        HistoryEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPersistenceWarning.Visibility = CurrentApp?.MarketWatchHistoryPersistenceFailed == true ? Visibility.Visible : Visibility.Collapsed;
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
        updated.Enabled = existing.Enabled;
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
    private void AttachPreview()
    {
        if (_previewSettings is not null) _previewSettings.PropertyChanged -= PreviewSettingsChanged;
        _previewSettings = (DataContext as AppSettings)?.MarketWatchNotifications;
        if (_previewSettings is not null) _previewSettings.PropertyChanged += PreviewSettingsChanged;
        RefreshPreview();
    }
    private void PreviewSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshPreview();
    private void RefreshPreview()
    {
        if (_previewSettings is not { } config) return;
        if (_previewIconKey != config.IconImageBase64)
        {
            _previewIconKey = config.IconImageBase64;
            _previewIcon = string.IsNullOrWhiteSpace(config.IconImageBase64)
                ? new System.Windows.Media.Imaging.BitmapImage(new Uri("https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/items/730.png"))
                : new Converters.Base64ToImageSourceConverter().Convert(config.IconImageBase64, typeof(ImageSource), null!, System.Globalization.CultureInfo.CurrentCulture) as ImageSource;
        }
        PreviewNotification = new(MarketWatchService.FormatText(config.TextTemplate, new(730, "1carat Diamond", 5000)), _previewIcon, DateTime.MaxValue);
    }
    private void Preview_Click(object sender, RoutedEventArgs e) { RefreshPreview(); CurrentApp?.PreviewMarketWatch(); }
    private void ListenSound_Click(object sender, RoutedEventArgs e) => CurrentApp?.AuditionMarketWatchSound();
    private void MoveOverlay_Click(object sender, RoutedEventArgs e) => CurrentApp?.BeginMarketWatchMove();
    private void Settings_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CurrentApp?.PersistMarketWatchSettings();
    private void Color_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || DataContext is not AppSettings settings) return;
        var fallback = box == BackgroundEditor ? "#DD102B50" : "#FFFFFFFF";
        try { if (ColorConverter.ConvertFromString(box.Text) is not Color) throw new FormatException(); }
        catch { box.Text = fallback; box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); }
    }
}
