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
        var rows = _workspace.Active.Where(q => q.Progress.IsVisibleInOverlay).Take(Math.Min(3,options.MaxTrackedQuests)).ToList();
        if (rows.Count == 0)
        {
            // Preview-only examples: never add these to character progress.
            foreach(var example in new[] {("Lost Child Quest [1/13]", "Talk to Vincent (ra_in01 384,246).", "ra_in01 384,246"), ("Sign Quest [48/81]", "Go inside a Niflheim building (niflheim 187 241).", "niflheim 187,241")}.Take(options.MaxTrackedQuests))
            {
                PreviewPanel.Children.Add(new TextBlock {Text=example.Item1,Foreground=color,FontWeight=FontWeights.SemiBold,FontSize=options.FontSize});
                PreviewPanel.Children.Add(new TextBlock {Text=example.Item2,Foreground=color,FontSize=options.FontSize,TextWrapping=TextWrapping.Wrap,LineHeight=options.FontSize+3,MaxHeight=(options.FontSize+3)*options.DescriptionLines});
                if(options.ShowCoordinates)PreviewPanel.Children.Add(new TextBlock {Text="⌖ "+example.Item3,Foreground=color});
                AddPreviewButtons(options.ShowButtons);
                PreviewPanel.Children.Add(new Border {Height=options.CardGap});
            }
            return;
        }
        foreach (var row in rows)
        {
            PreviewPanel.Children.Add(new TextBlock {Text=row.Title+" "+row.Position,Foreground=color,FontSize=options.FontSize,FontWeight=FontWeights.SemiBold});
            PreviewPanel.Children.Add(new TextBlock {Text=row.Instruction,TextWrapping=TextWrapping.Wrap,FontSize=options.FontSize,LineHeight=options.FontSize+3,MaxHeight=(options.FontSize+3)*options.DescriptionLines,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=color,Margin=new Thickness(0,0,0,options.CardGap)});
            if (options.ShowCoordinates)
            {
                var points = row.Guide?.Steps.FirstOrDefault(s => s.Number == row.Progress.CurrentStep)?.Locations;
                foreach (var point in points ?? []) PreviewPanel.Children.Add(new TextBlock { Text = (point.Map ?? T("QMapUnknown")) + $" {point.X},{point.Y}", TextWrapping = TextWrapping.Wrap, Foreground = color });
            }
            AddPreviewButtons(options.ShowButtons);
        }
    }
    private void AddPreviewButtons(bool visible)
    {
        if(!visible)return;
        var buttons=new WrapPanel();
        foreach(var text in new[]{"prev","next","more","navi","done"})
            buttons.Children.Add(new Button {Content=text,IsEnabled=false,FontSize=11,Height=22,MinHeight=22,Padding=new Thickness(5,0,5,0)});
        PreviewPanel.Children.Add(buttons);
    }
}
