using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoAura.Services;

namespace RoAura;

public partial class SkillIconPickerWindow : Window
{
    private readonly SkillIconCatalog _catalog = SkillIconCatalog.Instance;
    internal SkillIconCatalog.Skill? SelectedIcon { get; private set; }

    public SkillIconPickerWindow(int skillId)
    {
        InitializeComponent();
        var allJobs = new SkillIconCatalog.Job(-1, LocalizationService.Get("SkillIconAllJobs"), null, []);
        JobBox.ItemsSource = _catalog.Jobs.Prepend(allJobs);
        JobBox.SelectedItem = allJobs;
        if (_catalog.Skills.Any(skill => skill.Id == skillId)) SearchBox.Text = skillId.ToString();
        Loaded += (_, _) => { SearchBox.Focus(); SearchBox.SelectAll(); };
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (Results is null || EmptyText is null) return;
        var job = JobBox?.SelectedItem as SkillIconCatalog.Job;
        var matches = _catalog.Search(SearchBox.Text, job is { Id: >= 0 } ? job : null).ToArray();
        Results.ItemsSource = matches;
        Results.SelectedIndex = matches.Length == 1 ? 0 : -1;
        EmptyText.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (ChooseButton is not null) ChooseButton.IsEnabled = Results.SelectedItem is SkillIconCatalog.Skill;
    }

    private void Choose_Click(object sender, RoutedEventArgs e) => Choose();

    private void ResultDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(Results, source) is ListBoxItem)
            Choose();
    }

    private void Choose()
    {
        if (Results.SelectedItem is not SkillIconCatalog.Skill skill) return;
        SelectedIcon = skill;
        DialogResult = true;
    }
}
