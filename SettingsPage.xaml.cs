using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RoAura.Models;

namespace RoAura;

public partial class SettingsPage : UserControl
{
    private AppSettings? _observed;
    private MainWindow? Host => Window.GetWindow(this) as MainWindow;
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { Attach(); Host?.RefreshCharacterManager(); RefreshNavigation(); };
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
        Unloaded += (_, _) => { if (_observed is not null) _observed.PropertyChanged -= SettingsChanged; _observed = null; };
        SettingsTabs.SelectionChanged += (_, e) => { if (e.Source == SettingsTabs) { Host?.RefreshCharacterManager(); RefreshNavigation(); } };
    }
    private void Attach()
    {
        if (_observed is not null) _observed.PropertyChanged -= SettingsChanged;
        _observed = DataContext as AppSettings;
        if (_observed is not null) _observed.PropertyChanged += SettingsChanged;
    }
    private void SettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.ServerProfile) or nameof(AppSettings.PacketCaptureEnabled) or "") RefreshNavigation();
    }
    private void Server_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded && ServerChoice.SelectedValue is string value) Host?.SelectSettingsServer(value); }
    private void Language_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded && LanguageChoice.SelectedValue is string value) Host?.SelectSettingsLanguage(value); }
    private void Connection_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string action }) Host?.SettingsConnectionAction(action); }
    private void Save_Changed(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(() => Host?.SaveSharedSettings());
    private void Character_EditEnding(object sender, DataGridCellEditEndingEventArgs e) => Dispatcher.BeginInvoke(() => Host?.SaveSharedSettings());
    private void RefreshNavigation()
    {
        if (DataContext is not AppSettings settings || NavigationChoices is null) return;
        settings.EnsureNavigationSections();
        NavigationChoices.Children.Clear();
        string[] labels = ["Home", "Cooldowns", "Effects", "Reminders", "Equipment", "MvpTimers", "LootRewards", "GoldGoblin", "Database", "SettingsTitle", "Quests"];
        foreach (var preference in settings.NavigationSections.Where(p => p.Feature is not (AppFeature.Home or AppFeature.Settings) && (int)p.Feature < labels.Length))
        {
            var box = new CheckBox { Margin = new Thickness(0, 6, 0, 6), DataContext = preference,
                IsEnabled = FeatureAvailability.CanUse(settings.ServerProfile, settings.PacketCaptureEnabled, preference.Feature) };
            box.SetResourceReference(ContentControl.ContentProperty, labels[(int)preference.Feature]);
            box.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(NavigationPreference.Visible)) { Mode = BindingMode.TwoWay });
            box.Click += (_, _) => Host?.SaveSharedSettings();
            NavigationChoices.Children.Add(box);
        }
    }
}
