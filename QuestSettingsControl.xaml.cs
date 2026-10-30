using System.ComponentModel; using System.Windows; using System.Windows.Controls; using System.Windows.Media; using RoAura.Services;
namespace RoAura;
public partial class QuestSettingsControl : UserControl
{
 private QuestWorkspace? _workspace; private Action? _save;
 public event Action? OpenBrowserRequested, ToggleTrackerRequested, MoveTrackerRequested;
 public QuestSettingsControl() => InitializeComponent();
 public void Initialize(QuestWorkspace workspace, Func<CharacterSessionSnapshot> observed, Action saveVisuals)
 { _workspace=workspace;_save=saveVisuals;SettingsRoot.DataContext=workspace;workspace.Changed+=RenderPreview;workspace.Settings.QuestOverlay.PropertyChanged+=VisualsChanged;RenderPreview(); }
 public void Detach() { if(_workspace is null)return;_workspace.Changed-=RenderPreview;_workspace.Settings.QuestOverlay.PropertyChanged-=VisualsChanged; }
 private void VisualsChanged(object? sender, PropertyChangedEventArgs e) { _save?.Invoke();RenderPreview(); }
 private static string T(string key)=>LocalizationService.Get(key);
 public void Report(string text)=>GameStatus.Text=text;
 private async void Reload_Click(object sender,RoutedEventArgs e) {if(_workspace is not null)await _workspace.LoadCatalogAsync();}
 private void Open_Click(object sender,RoutedEventArgs e)=>OpenBrowserRequested?.Invoke();
 private void Tracker_Click(object sender,RoutedEventArgs e)=>ToggleTrackerRequested?.Invoke();
 private void Move_Click(object sender,RoutedEventArgs e)=>MoveTrackerRequested?.Invoke();
    private void RenderPreview()
    {
        if (_workspace is null) return;
        CatalogStatus.Text=_workspace.IsLoadingCatalog?T("QLoading"):_workspace.Catalog is null?T("QCatalogMissing"):T("QCatalogReady")+" · v5 · "+_workspace.Catalog.Guides.Count;
        PreviewPanel.Children.Clear();
        PreviewPanel.Children.Add(new TextBlock {Text="ROQuestie  ⋮⋮",Foreground=Brushes.White,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
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
            PreviewPanel.Children.Add(new TextBlock {Text=row.Title+" "+row.Position,Foreground=color,FontSize=options.FontSize,FontWeight=FontWeights.SemiBold});
            PreviewPanel.Children.Add(new TextBlock {Text=row.Instruction,TextWrapping=TextWrapping.Wrap,FontSize=options.FontSize,LineHeight=options.FontSize+3,MaxHeight=(options.FontSize+3)*options.DescriptionLines,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=color,Margin=new Thickness(0,0,0,options.CardGap)});
            if (options.ShowCoordinates)
            {
                var points = row.Guide?.Steps.FirstOrDefault(s => s.Number == row.Progress.CurrentStep)?.Locations;
                foreach (var point in points ?? []) PreviewPanel.Children.Add(new TextBlock { Text = (point.Map ?? T("QMapUnknown")) + $" {point.X},{point.Y}", TextWrapping = TextWrapping.Wrap, Foreground = color });
            }
            if (options.ShowButtons)
            {
                var buttons = new WrapPanel();
                foreach (var text in new[] { "Prev", "Next", "More", "navi", "Done" }) buttons.Children.Add(new Button { Content = text, IsEnabled = false, FontSize = 10, MinHeight = 22, Height = 22, Padding = new Thickness(5, 0, 5, 0) });
                PreviewPanel.Children.Add(buttons);
            }
        }
    }
}
