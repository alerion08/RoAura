using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public sealed record QuestCategoryChoice(string Caption, string? Key);
public sealed record QuestCatalogListRow(QuestGuide Guide)
{
    public string Title => Guide.Title;
    public string Summary => LocalizationService.Get("QCategory" + Guide.Category) + " · " +
        LocalizationService.Get("QBaseLevel") + " " + (Guide.BaseLevel?.ToString() ?? LocalizationService.Get("QUnknown")) +
        " · " + Guide.Steps.Count + " " + LocalizationService.Get("QSteps") + "\n" + LocalizationService.Get("QUnverified");
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
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Filter(); };
        Loaded += async (_, _) => { if (_workspace?.Catalog is null && _workspace?.IsLoadingCatalog == false) await _workspace.LoadCatalogAsync(); Render(); };
        Unloaded += (_, _) => _searchTimer.Stop();
    }
    public void Initialize(QuestWorkspace workspace, Func<CharacterSessionSnapshot> observed, Action saveVisuals)
    {
        _workspace = workspace; _observed = observed; _saveVisuals = saveVisuals;
        WorkspaceRoot.DataContext = workspace;
        workspace.Changed += Render;
        workspace.Settings.QuestOverlay.PropertyChanged += Visuals_Changed;
        Render();
    }
    public void Detach()
    {
        _searchTimer.Stop();
        if (_workspace is null) return;
        _workspace.Changed -= Render;
        _workspace.Settings.QuestOverlay.PropertyChanged -= Visuals_Changed;
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
            CatalogStatus.Text = _workspace.IsLoadingCatalog ? T("QLoading") : _workspace.Catalog is null ? T("QCatalogMissing") :
                T("QCatalogReady") + " · v5 · " + _workspace.Catalog.Guides.Count;
            ActiveEmpty.Visibility = _workspace.Active.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            HistoryEmpty.Visibility = _workspace.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CatalogDetails.Render(_workspace); ActiveDetails.Render(_workspace);
            Filter(); RenderPreview();
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
                UseLevel.IsChecked == true ? (int)FilterLevel : null).Select(g => new QuestCatalogListRow(g)).ToList() ?? [];
            CatalogList.ItemsSource = rows;
            CatalogList.SelectedItem = rows.FirstOrDefault(r => r.Guide.Key == _workspace.SelectedGuide?.Key);
            CatalogEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updating = old; }
    }
    private void Guide_Changed(object sender, SelectionChangedEventArgs e)
    { if (!_updating && _workspace is not null && CatalogList.SelectedItem is QuestCatalogListRow row) _workspace.SelectedGuide = row.Guide; }
    private void Search_Changed(object sender, TextChangedEventArgs e) { _searchTimer.Stop(); _searchTimer.Start(); }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (!_updating) Filter(); }
    private void Filter_Click(object sender, RoutedEventArgs e) => Filter();
    private void Level_Changed(object sender, KeyboardFocusChangedEventArgs e) => Filter();
    private void Level_KeyUp(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Filter(); }
    private async void Profile_Changed(object sender, SelectionChangedEventArgs e)
    { if (!_updating && _workspace is not null && ProfileChoice.SelectedItem is QuestProfileChoice choice) await _workspace.SelectProfileAsync(choice.Key, _observed!()); }
    private async void Reload_Click(object sender, RoutedEventArgs e) { if (_workspace is not null) await _workspace.LoadCatalogAsync(); }
    private async void Save_Click(object sender, RoutedEventArgs e) { if (_workspace is not null) await _workspace.FlushAsync(); }
    private void Visibility_Click(object sender, RoutedEventArgs e)
    { if (sender is CheckBox { DataContext: QuestListRow row } box) _workspace?.SetVisibility(row, box.IsChecked == true); }
    private void QuestAction_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null || sender is not Button { Tag: string action, DataContext: QuestListRow row }) return;
        switch (action)
        {
            case "prev": _workspace.Move(row, -1); break;
            case "next": _workspace.Move(row, 1); break;
            case "more": _workspace.SelectedGuide = row.Guide; QuestTabs.SelectedIndex = 1; break;
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
    private void Visuals_Changed(object? sender, PropertyChangedEventArgs e) { _saveVisuals?.Invoke(); RenderPreview(); }
    private void RenderPreview()
    {
        if (_workspace is null) return;
        PreviewPanel.Children.Clear();
        var options = _workspace.Settings.QuestOverlay;
        PreviewPanel.Width = options.Width;
        PreviewPanel.Opacity = options.OpacityPercent / 100;
        PreviewPanel.LayoutTransform = new ScaleTransform(options.Scale, options.Scale);
        Brush color;
        try { color = (Brush)new BrushConverter().ConvertFromString(options.TextColor)!; }
        catch { color = Brushes.White; }
        var rows = _workspace.Active.Where(q => q.Progress.IsVisibleInOverlay).Take(3).ToList();
        if (rows.Count == 0) { PreviewPanel.Children.Add(new TextBlock { Text = T("QPreviewEmpty"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White }); return; }
        foreach (var row in rows)
        {
            PreviewPanel.Children.Add(new TextBlock { Text = row.Title + " " + row.Position + "\n" + row.Instruction,
                TextWrapping = TextWrapping.Wrap, FontSize = options.FontSize, Foreground = color, Margin = new Thickness(0, 0, 0, options.CardGap) });
            if (options.ShowCoordinates)
            {
                var points = row.Guide?.Steps.FirstOrDefault(s => s.Number == row.Progress.CurrentStep)?.Locations;
                foreach (var point in points ?? []) PreviewPanel.Children.Add(new TextBlock { Text = (point.Map ?? T("QMapUnknown")) + $" {point.X},{point.Y}", TextWrapping = TextWrapping.Wrap, Foreground = color });
            }
            if (options.ShowButtons)
            {
                var buttons = new WrapPanel();
                foreach (var text in new[] { "Prev", "Next", "More", "navi" }) buttons.Children.Add(new Button { Content = text, IsEnabled = false, FontSize = 10, MinHeight = 22, Height = 22, Padding = new Thickness(5, 0, 5, 0) });
                PreviewPanel.Children.Add(buttons);
            }
        }
    }
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
            foreach (var (tab, name) in new[] { (1, "active"), (2, "history"), (3, "settings") })
            {
                QuestTabs.SelectedIndex = tab;
                SaveSnapshot(output, language + "-" + name, 1);
            }
        }
        await workspace.FlushAsync();
    }
    private void SaveSnapshot(string directory, string name, double scale)
    {
        // Render at equivalent pixel densities, not a claim of mixed-monitor OS DPI acceptance.
        Measure(new Size(840, 560)); Arrange(new Rect(0, 0, 840, 560)); UpdateLayout();
        Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(840 * scale), (int)(560 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }

}
