using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoAura.Services;

namespace RoAura;

public partial class SkillIconPickerWindow : Window
{
    private readonly SkillIconCatalog _catalog = SkillIconCatalog.Instance;
    internal SkillIconCatalog.Skill? SelectedIcon { get; private set; }

    private bool _updatingJobs;
    private bool IncludeRebirth => RebirthBox.IsChecked == true;
    private bool IncludeRenewal => RenewalBox.IsChecked == true;

    public SkillIconPickerWindow()
    {
        InitializeComponent();
        UpdateJobs();
        Loaded += (_, _) => SearchBox.Focus();
    }

    internal static SkillIconCatalog.Skill? Pick(Window owner)
    {
        var dialog = new SkillIconPickerWindow { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedIcon : null;
    }

    private void JobSearchChanged(object sender, RoutedEventArgs e) => UpdateJobs();
    private void JobEditionChanged(object sender, RoutedEventArgs e) => UpdateJobs();

    private void UpdateJobs()
    {
        if (JobBox is null || RebirthBox is null || RenewalBox is null) return;
        var previous = (JobBox.SelectedItem as ComboBoxItem)?.Tag;
        var jobs = _catalog.FindJobs(JobSearchBox.Text, IncludeRebirth, IncludeRenewal)
            .Select(job => new ComboBoxItem { Content = job.Name, Tag = job }).ToArray();
        _updatingJobs = true;
        JobBox.ItemsSource = jobs.Prepend(new ComboBoxItem { Content = LocalizationService.Get("SkillIconAllJobs") });
        JobBox.SelectedItem = jobs.FirstOrDefault(item => Equals(item.Tag, previous)) ??
            (jobs.Length == 1 && JobSearchBox.Text.Trim().Length > 0 ? jobs[0] : JobBox.Items[0]);
        _updatingJobs = false;
        RefreshResults();
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshResults();

    private void RefreshResults()
    {
        if (_updatingJobs || Results is null || EmptyText is null || RebirthBox is null || RenewalBox is null) return;
        var job = (JobBox.SelectedItem as ComboBoxItem)?.Tag as SkillIconCatalog.Job;
        var matches = _catalog.Search(SearchBox.Text, job, IncludeRebirth, IncludeRenewal).ToArray();
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
