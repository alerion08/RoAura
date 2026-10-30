using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RoAura.Models;

namespace RoAura.Controls;

public partial class CharacterScopeEditor : UserControl
{
    public static readonly DependencyProperty ScopeProperty = DependencyProperty.Register(nameof(Scope), typeof(CharacterScope), typeof(CharacterScopeEditor), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(nameof(Settings), typeof(AppSettings), typeof(CharacterScopeEditor), new PropertyMetadata(null, Changed));
    public CharacterScope? Scope { get => (CharacterScope?)GetValue(ScopeProperty); set => SetValue(ScopeProperty, value); }
    public AppSettings? Settings { get => (AppSettings?)GetValue(SettingsProperty); set => SetValue(SettingsProperty, value); }
    public CharacterScopeEditor() { InitializeComponent(); Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach(); }
    private CharacterScope? _scope;
    private AppSettings? _settings;
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) { var view = (CharacterScopeEditor)d; if (view.IsLoaded) view.Attach(); }
    private void Attach()
    {
        Detach(); _scope = Scope; _settings = Settings;
        if (_scope is not null) _scope.Characters.CollectionChanged += SelectionChanged;
        if (_settings is not null) _settings.Characters.CollectionChanged += ProfilesChanged;
        Rebuild();
    }
    private void Detach()
    {
        if (_scope is not null) _scope.Characters.CollectionChanged -= SelectionChanged;
        if (_settings is not null) _settings.Characters.CollectionChanged -= ProfilesChanged;
        _scope = null; _settings = null;
    }
    private void ProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();
    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in Choices.Items.OfType<Choice>()) row.Refresh();
        (Window.GetWindow(this) as MainWindow)?.SaveSharedSettings();
    }
    private void Rebuild()
    {
        if (Choices is null) return;
        Choices.ItemsSource = _settings?.Characters.Select(p => new Choice(p, _scope)).ToList();
        EmptyHint.Visibility = _settings?.Characters.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void All_Click(object sender, RoutedEventArgs e) => Scope?.Characters.Clear();
    public sealed class Choice(CharacterProfile profile, CharacterScope? scope) : INotifyPropertyChanged
    {
        public CharacterProfile Profile => profile;
        public bool Selected
        {
            get => scope?.Characters.Contains(profile.Id) == true;
            set { if (scope is null || value == Selected) return; if (value) scope.Characters.Add(profile.Id); else scope.Characters.Remove(profile.Id); Refresh(); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
    }
}
