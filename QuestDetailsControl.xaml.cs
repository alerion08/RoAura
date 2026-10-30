using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Services;

namespace RoAura;

public partial class QuestDetailsControl : UserControl
{
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
        Requirements.Text = string.IsNullOrWhiteSpace(guide?.Requirements) ? T("QUnknown") : guide.Requirements;
        Rewards.Text = string.IsNullOrWhiteSpace(guide?.Rewards) ? T("QUnknown") : guide.Rewards;
        SourceText.Text = guide is null ? T("QUnknown") : guide.SourceUrl + "\n\n" + T("QReview") + ": " + guide.ReviewState +
            "\n" + T("QSourceHash") + ": " + (guide.ContentHash ?? T("QUnknown")) + "\n" +
            T("QFreshness") + ": " + guide.RefreshState + "\n" + (guide.LastSuccessUtc ?? T("QUnknown")) + "\n" + guide.LastError + "\n\n" + T("QEvidenceHint");
        AddButton.Content = T(workspace.IsActive(guide) ? "QActive" : workspace.HasProgress(guide) ? "QInHistory" : "QAdd");
        AddButton.IsEnabled = workspace.CanEdit && guide?.Steps.Count > 0 && !workspace.HasProgress(guide);
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
                StepsPanel.Children.Add(new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 6),
                    Background = (Brush)FindResource(workspace.IsCurrentStep(step.Number) ? "RoTitleBrush" : "RoCardBrush"),
                    BorderBrush = (Brush)FindResource("RoSoftBorderBrush"), BorderThickness = new Thickness(1), Child = content });
            }
        WalkScroll.ScrollToVerticalOffset(offset);
    }
    private static TextBlock Text(string value, bool bold = false) => new() { Text = value, TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 2, 0, 3) };
    internal bool HasStepCount(int count) => StepsPanel.Children.Count == count;
    private void Add_Click(object sender, RoutedEventArgs e) => _workspace?.AddSelected();
    private void Source_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_workspace?.SelectedGuide?.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "RoAura", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
