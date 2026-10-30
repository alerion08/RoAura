using System.Diagnostics;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Services;

namespace RoAura;

public partial class QuestDetailsControl : UserControl
{
    public event Action<RoAura.Models.QuestGuide?>? GuideRequested;
    private QuestWorkspace? _workspace;
    private string _stamp = "";
    private string? _lastKey;
    public QuestDetailsControl() => InitializeComponent();
    private static string T(string key) => LocalizationService.Get(key);
    public void Render(QuestWorkspace workspace)
    {
        _workspace = workspace;
        var guide = workspace.SelectedGuide;
        var stamp = guide?.Key + "|" + guide?.ContentHash + "|" + workspace.CanEdit + "|" + workspace.IsActive(guide) + "|" +
            LocalizationService.CurrentLanguage + "|" + string.Join(",", workspace.Active.Concat(workspace.History).Where(q => q.Progress.QuestKey == guide?.Key).Select(q => q.Progress.CurrentStep));
        if (_stamp == stamp) return;
        _stamp = stamp;
        var offset = _lastKey == guide?.Key ? WalkScroll.VerticalOffset : 0;
        _lastKey = guide?.Key;
        QuestTitle.Text = guide?.Title ?? T("QSelectGuide");
        Metadata.Text = guide is null ? "" : T("QUnverified") + " · " + T("QFreshness") + ": " + guide.RefreshState + " · " + T("QBaseLevel") + " " + (guide.BaseLevel?.ToString() ?? T("QUnknown")) + " · Job Lv. " + (guide.JobLevel?.ToString() ?? T("QUnknown"));
        FactsPanel.Children.Clear();
        RenderFacts("QRequirements", guide?.Requirements);
        RenderFacts("QRewards", guide?.Rewards);
        SourceText.Text = guide is null ? T("QUnknown") : guide.SourceUrl + "\n\n" + T("QReview") + ": " + guide.ReviewState +
            "\n" + T("QSourceHash") + ": " + (guide.ContentHash ?? T("QUnknown")) + "\n" +
            T("QFreshness") + ": " + guide.RefreshState + "\n" + (guide.LastSuccessUtc ?? T("QUnknown")) + "\n" + guide.LastError + "\n\n" + T("QEvidenceHint");
        AddButton.Content = T(workspace.IsActive(guide) ? "QActive" : workspace.HasProgress(guide) ? "QRestore" : "QAdd");
        AddButton.IsEnabled = workspace.CanEdit && guide?.Steps.Count > 0;
        StepsPanel.Children.Clear();
        if (guide is null || guide.Steps.Count == 0) StepsPanel.Children.Add(Text(T("QNoSteps")));
        if (guide is not null)
            foreach (var step in guide.Steps)
            {
                var content = new StackPanel();
                content.Children.Add(Text($"{step.Number}. " + (step.SourceNumber is null ? T("QNarrative") : ""), true));
                if (!string.IsNullOrWhiteSpace(step.Section)) content.Children.Add(Text(step.Section));
                content.Children.Add(Text(step.Instruction));
                foreach (var location in step.Locations)
                    content.Children.Add(Text(T("QWikiCoordinates") + ": " + (location.Map ?? T("QMapUnknown")) +
                        $" {location.X},{location.Y}"));
                var select = new Button { Content = T("QSelectStep"), IsEnabled = workspace.CanEdit && workspace.HasProgress(guide),
                    HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Tag = step.Number };
                select.Click += (_, _) => workspace.SelectStep((int)select.Tag);
                content.Children.Add(select);
                foreach(var answer in QuestAnswers.Extract(step.Instruction))
                {
                    var copy=new Button {Content=T("QCopyAnswer"),ToolTip=answer,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,6,0,0)};
                    copy.Click+=(_,_)=>{if(ClipboardService.TryCopy(answer)){QuestCopyFeedback.Success(copy);}else copy.ToolTip=T("QCopyFailed");};
                    content.Children.Add(copy);
                }
                StepsPanel.Children.Add(new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 6),
                    Background = (Brush)FindResource(workspace.IsCurrentStep(step.Number) ? "RoTitleBrush" : "RoCardBrush"),
                    BorderBrush = (Brush)FindResource(workspace.IsCurrentStep(step.Number) ? "RoGoldBrush" : "RoSoftBorderBrush"),
                    BorderThickness = new Thickness(workspace.IsCurrentStep(step.Number) ? 2 : 1), Child = content, Tag = step.Number });
            }
        WalkScroll.ScrollToVerticalOffset(offset);
        if (guide is not null) Dispatcher.BeginInvoke(ScrollToCurrent, DispatcherPriority.Loaded);
    }
    private void RenderFacts(string title, string? original)
    {
        var group = new StackPanel { Margin = new Thickness(0,0,0,18) };
        var heading = Text(T(title), true); heading.Style=(Style)FindResource("RoSectionTitleStyle");group.Children.Add(heading);
        var facts = QuestFactFormatter.Parse(original);
        if (facts.Count == 0) group.Children.Add(Text(T("QUnknown")));
        foreach(var fact in facts)
        {
            var card = new StackPanel();
            if (fact.Label.Length>0)
            {
                var label=Text(QuestFactFormatter.LabelKey(fact.Label) is { } key ? T(key) : fact.Label,true);
                label.ToolTip=fact.Label;card.Children.Add(label);
            }
            if(string.IsNullOrWhiteSpace(fact.Value))card.Children.Add(Text(T("QUnknown")));
            foreach(var line in QuestFactFormatter.Lines(fact))
            {
                var block = Text("",false); block.Margin=new Thickness(12,4,0,5);
                foreach(var part in QuestLinks.Split(line,_workspace?.Catalog,_workspace?.SelectedGuide?.Key))
                {
                    if(part.Guide is { } linked)
                    {
                        var link=new Hyperlink(new Run(part.Text)) {ToolTip=linked.Title};
                        link.Click+=(_,_)=>GuideRequested?.Invoke(linked);block.Inlines.Add(link);
                    }
                    else AddNumbers(block,part.Text);
                }
                card.Children.Add(block);
            }
            group.Children.Add(new Border { Child=card, Padding=new Thickness(12),Margin=new Thickness(0,8,0,0),CornerRadius=new CornerRadius(6),Background=(Brush)FindResource("RoTitleBrush") });
        }
        if (!string.IsNullOrWhiteSpace(original)) group.Children.Add(new Expander { Header=T("QOriginalText"),Content=Text(original),Margin=new Thickness(0,8,0,0) });
        FactsPanel.Children.Add(group);
    }
    private static void AddNumbers(TextBlock block,string text)
    {
        var last=0;
        foreach(Match match in Regex.Matches(text,@"\b\d[\d,]*(?:\.\d+)?(?:%|\s*(?:EXP|Zeny))?"))
        {block.Inlines.Add(new Run(text[last..match.Index]));block.Inlines.Add(new Bold(new Run(match.Value)));last=match.Index+match.Length;}
        block.Inlines.Add(new Run(text[last..]));
    }
    internal void ShowFacts()=>DetailsTabs.SelectedIndex=1;
    public void ShowCurrentStep()
    {
        DetailsTabs.SelectedIndex=0;
        Dispatcher.BeginInvoke(new Action(()=>{UpdateLayout();ScrollToCurrent();}),DispatcherPriority.ContextIdle);
    }
    public void ScrollToCurrent()
    {
        if(Window.GetWindow(this) is {IsVisible:false})return;
        foreach(var card in StepsPanel.Children.OfType<Border>())
            if(card.Tag is int step && _workspace?.IsCurrentStep(step)==true) {WalkScroll.UpdateLayout();var top=card.TranslatePoint(new Point(0,0),StepsPanel).Y;WalkScroll.ScrollToVerticalOffset(top);return;}
    }
    internal bool CurrentStepInView
    {
        get
        {
            if(DetailsTabs.SelectedIndex!=0)return false;
            var card=StepsPanel.Children.OfType<Border>().FirstOrDefault(c=>c.Tag is int n && _workspace?.IsCurrentStep(n)==true);
            if(card is null)return false;
            var top=card.TranslatePoint(new Point(0,0),StepsPanel).Y-WalkScroll.VerticalOffset;
            return top>=-1 && top<WalkScroll.ViewportHeight;
        }
    }
    private void Current_Click(object sender,RoutedEventArgs e)=>ScrollToCurrent();
    private static TextBlock Text(string value, bool bold = false) => new() { Text = value, TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 2, 0, 3) };
    internal bool HasStepCount(int count) => StepsPanel.Children.Count == count;
    private void Add_Click(object sender, RoutedEventArgs e) {if(_workspace?.SelectedGuide is { } guide)_workspace.ToggleTracking(guide);}
    private void Source_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_workspace?.SelectedGuide?.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "RoAura", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
