using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public sealed record QuestCategoryChoice(string Caption, string? Key)
{ public override string ToString() => Caption; }
public sealed record QuestCatalogListRow(QuestGuide Guide, bool Tracked, bool CanTrack)
{
    public string Title => Guide.Title;
    public string CategoryTag=>LocalizationService.Get("QCategory"+Guide.Category);
    public string LevelTag=>LocalizationService.Get("QBaseLevel")+" "+(Guide.BaseLevel?.ToString()??LocalizationService.Get("QUnknown"));
    public string StepsTag=>Guide.Steps.Count+" "+LocalizationService.Get("QSteps");
}

public partial class QuestsPage : UserControl
{
    private QuestWorkspace? _workspace;
    private Func<CharacterSessionSnapshot>? _observed;
    private Action? _saveVisuals;
    private bool _updating;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private double _filterLevel = 99;
    public double FilterLevel { get => _filterLevel; set => _filterLevel = double.IsFinite(value) ? Math.Clamp(value, 1, 1000) : _filterLevel; }
    public bool CanEdit => _workspace?.CanEdit == true;
    public QuestsPage()
    {
        InitializeComponent();
        CatalogDetails.GuideRequested += ShowGuide;
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Filter(); };
        Loaded += async (_, _) => { if (_workspace?.Settings.QuestOverlay.AutoLoadCatalog == true && _workspace.Catalog is null && _workspace?.IsLoadingCatalog == false) await _workspace.LoadCatalogAsync(); Render(); };
        Unloaded += (_, _) => _searchTimer.Stop();
    }
    public void Initialize(QuestWorkspace workspace, Func<CharacterSessionSnapshot> observed, Action saveVisuals)
    {
        _workspace = workspace; _observed = observed; _saveVisuals = saveVisuals;
        WorkspaceRoot.DataContext = workspace;
        workspace.Changed += Render;

        Render();
    }
    public void Detach()
    {
        _searchTimer.Stop();
        if (_workspace is null) return;
        _workspace.Changed -= Render;

    }
    private static string T(string key) => LocalizationService.Get(key);
    private void Render()
    {
        if (_workspace is null) return;
        _updating = true;
        try
        {
            var category = (CategoryChoice.SelectedItem as QuestCategoryChoice)?.Key;
            ProfileChoice.ItemsSource = _workspace.ProfileChoices();
            ProfileChoice.SelectedItem = ((IReadOnlyList<QuestProfileChoice>)ProfileChoice.ItemsSource).FirstOrDefault(p => p.Key == _workspace.ManualProfile);
            CategoryChoice.ItemsSource = new[] { new QuestCategoryChoice(T("QAllCategories"), null) }.Concat(
                _workspace.Catalog?.Guides.Values.Select(q => q.Category).Distinct().OrderBy(x => x)
                    .Select(c => new QuestCategoryChoice(T("QCategory" + c), c)) ?? [] ).ToArray();
            CategoryChoice.SelectedItem = ((IEnumerable<QuestCategoryChoice>)CategoryChoice.ItemsSource).FirstOrDefault(c => c.Key == category)
                ?? ((IEnumerable<QuestCategoryChoice>)CategoryChoice.ItemsSource).First();
            ActiveEmpty.Visibility = _workspace.Active.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            HistoryEmpty.Visibility = _workspace.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CatalogDetails.Render(_workspace);
            Filter();
        }
        finally { _updating = false; }
    }
    private void Filter()
    {
        if (_workspace is null) return;
        var old = _updating; _updating = true;
        try
        {
            var rows = _workspace.Catalog?.Search(SearchInput.Text, (CategoryChoice.SelectedItem as QuestCategoryChoice)?.Key,
                UseLevel.IsChecked == true ? (int)FilterLevel : null).Select(g => new QuestCatalogListRow(g, _workspace.IsActive(g), _workspace.CanEdit && g.Steps.Count > 0)).ToList() ?? [];
            CatalogList.ItemsSource = rows;
            CatalogList.SelectedItem = rows.FirstOrDefault(r => r.Guide.Key == _workspace.SelectedGuide?.Key);
            CatalogEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updating = old; }
    }
    private void Track_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: QuestCatalogListRow row }) { _workspace?.ToggleTracking(row.Guide); e.Handled=true; } }
    private void Guide_Changed(object sender, SelectionChangedEventArgs e)
    { if (!_updating && _workspace is not null && CatalogList.SelectedItem is QuestCatalogListRow row) _workspace.SelectedGuide = row.Guide; }
    private void Search_Changed(object sender, TextChangedEventArgs e) { _searchTimer.Stop(); _searchTimer.Start(); }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (!_updating) Filter(); }
    private void Filter_Click(object sender, RoutedEventArgs e) => Filter();
    private void Level_Changed(object sender, KeyboardFocusChangedEventArgs e) => Filter();
    private void Level_KeyUp(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Filter(); }
    private async void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && _workspace is not null && ProfileChoice.SelectedItem is QuestProfileChoice choice &&
            (choice.Key is null || string.Equals(choice.Key.Server, _workspace.Settings.ServerProfile, StringComparison.OrdinalIgnoreCase)))
            await _workspace.SelectProfileAsync(choice.Key, _observed!());
    }
    private async void Reload_Click(object sender, RoutedEventArgs e) { if (_workspace is not null) await _workspace.LoadCatalogAsync(); }
    private async void Save_Click(object sender, RoutedEventArgs e) { if (_workspace is not null) await _workspace.SaveAndRefreshAsync(_observed!()); }
    private void Visibility_Click(object sender, RoutedEventArgs e)
    { if (sender is CheckBox { DataContext: QuestListRow row } box) _workspace?.SetVisibility(row, box.IsChecked == true); }
    private void QuestAction_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null || sender is not Button { Tag: string action, DataContext: QuestListRow row }) return;
        switch (action)
        {
            case "prev": _workspace.Move(row, -1); break;
            case "next": _workspace.Move(row, 1); break;
            case "more": ShowGuide(row.Guide); break;
            case "up": _workspace.Reorder(row, -1); break;
            case "down": _workspace.Reorder(row, 1); break;
            case "pause": _workspace.SetState(row, QuestManualState.Paused); break;
            case "remove": _workspace.SetState(row, QuestManualState.Removed); break;
            case "restore": _workspace.SetState(row, QuestManualState.Active); break;
            case "complete":
                if (MessageBox.Show(Window.GetWindow(this), T("QCompleteConfirm"), "ROQuestie", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    _workspace.SetState(row, QuestManualState.Completed, true);
                break;
        }
    }
    public void ShowGuide(RoAura.Models.QuestGuide? guide)
    { if (_workspace is null) return; _workspace.SelectedGuide=guide; QuestTabs.SelectedIndex=3; CatalogDetails.ShowCurrentStep(); }
    private void OpenGuide_Click(object sender,RoutedEventArgs e)=>ShowGuide(_workspace?.SelectedGuide);
    private void OpenGuide_DoubleClick(object sender,MouseButtonEventArgs e)=>ShowGuide(_workspace?.SelectedGuide);
    internal Button? CurrentNavigationButton=>CatalogDetails.CurrentNavigationButton;
    internal bool CurrentStepInView=>CatalogDetails.CurrentStepInView;
    public void FocusSearch() {if(QuestTabs.SelectedIndex==0)SearchInput.Focus();}
    internal void ShowFacts(RoAura.Models.QuestGuide guide) {ShowGuide(guide);CatalogDetails.ShowFacts();}
    internal async Task ValidateSmokeAsync(QuestWorkspace workspace)
    {
        _workspace = workspace;
        SearchInput.Text = "Lost Child"; _searchTimer.Stop(); Filter();
        if (CatalogList.Items.Count != 1) throw new InvalidOperationException("Quest title search failed.");
        CatalogList.SelectedIndex = 0;
        CatalogDetails.Render(workspace);
        if (!CatalogDetails.HasStepCount(13)) throw new InvalidOperationException("Quest details lost source instructions.");
        var output = Environment.GetEnvironmentVariable("ROAURA_QUEST_UI_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        foreach (var language in new[] { "ru", "en" })
        {
            workspace.Settings.Language = language;
            LocalizationService.ApplyLanguage(language); workspace.RefreshLanguage();
            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                QuestTabs.SelectedIndex = 0;
                SaveSnapshot(output, language + "-catalog-" + (int)(scale * 100), scale);
            }
            foreach (var (tab, name) in new[] { (1, "active"), (2, "history"), (3, "walkthrough") })
            {
                QuestTabs.SelectedIndex = tab;
                SaveSnapshot(output, language + "-" + name, 1);
            }
        }
        await workspace.FlushAsync();
    }
    private void SaveSnapshot(string directory, string name, double scale)
    {
        // A detached view cannot be re-arranged by MainWindow's larger parent slot.
        // Equivalent pixel densities are not real mixed-monitor OS DPI acceptance.
        var view = new QuestsPage();
        view.Initialize(_workspace!, () => default, () => { });
        try
        {
            view.SearchInput.Text = SearchInput.Text; view._searchTimer.Stop(); view.Filter();
            view.QuestTabs.SelectedIndex = QuestTabs.SelectedIndex;
            view.Measure(new Size(840, 560)); view.Arrange(new Rect(0, 0, 840, 560)); view.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(840 * scale), (int)(560 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
        }
        finally { view.Detach(); }
    }

}
