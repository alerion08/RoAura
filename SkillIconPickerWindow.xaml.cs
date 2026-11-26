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
    private SkillIconCatalog.Job? _selectedJob;
    private string AllJobs => LocalizationService.Get("SkillIconAllJobs");
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

    private void ClosePicker_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Picker_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!JobField.IsMouseOver && !JobOptions.IsMouseOver) JobPopup.IsOpen = false;
    }

    private void Picker_Deactivated(object? sender, EventArgs e) => JobPopup.IsOpen = false;

    private void JobField_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (JobOptions.IsMouseOver || !JobField.IsMouseOver) return;
        if (!JobPopup.IsOpen) OpenJobs("");
        if (!JobArrow.IsMouseOver) return;
        JobEditor.Focus();
        JobEditor.CaretIndex = JobEditor.Text.Length;
        e.Handled = true;
    }

    private void JobEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingJobs || JobPopup is null) return;
        _selectedJob = null;
        OpenJobs(JobEditor.Text);
        RefreshResults();
    }

    private void JobEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && JobPopup.IsOpen)
        {
            JobPopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Up)
        {
            if (!JobPopup.IsOpen) OpenJobs("");
            if (JobOptions.HasItems)
            {
                var index = JobOptions.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                JobOptions.SelectedIndex = Math.Clamp(index, 0, JobOptions.Items.Count - 1);
                JobOptions.ScrollIntoView(JobOptions.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && JobPopup.IsOpen)
        {
            if (JobOptions.SelectedItem is string name) SelectJob(name);
            e.Handled = true;
        }
    }

    private void JobOptions_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(JobOptions, source) is ListBoxItem { Content: string name })
            SelectJob(name);
    }

    private void SelectJob(string name)
    {
        _selectedJob = _catalog.Jobs.FirstOrDefault(job => job.Name == name);
        JobPopup.IsOpen = false;
        SetJobEditorText(_selectedJob?.Name ?? "");
        JobEditor.Focus();
        RefreshResults();
    }

    private void JobEditionChanged(object sender, RoutedEventArgs e) => UpdateJobs();

    private void UpdateJobs()
    {
        JobPopup.IsOpen = false;
        if (_selectedJob is not null && !_catalog.FindJobs("", IncludeRebirth, IncludeRenewal).Contains(_selectedJob))
            _selectedJob = null;
        SetJobEditorText(_selectedJob?.Name ?? "");
        RefreshResults();
    }

    private void SetJobEditorText(string text)
    {
        _updatingJobs = true;
        try
        {
            JobEditor.Text = text;
            JobEditor.CaretIndex = text.Length;
        }
        finally { _updatingJobs = false; }
    }

    private void OpenJobs(string query)
    {
        var names = _catalog.FindJobs(query, IncludeRebirth, IncludeRenewal).Select(job => job.Name);
        JobOptions.ItemsSource = (string.IsNullOrWhiteSpace(query) ? names.Prepend(AllJobs) : names).ToArray();
        JobOptions.SelectedIndex = -1;
        JobPopup.IsOpen = JobOptions.HasItems;
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshResults();

    private void RefreshResults()
    {
        if (_updatingJobs || Results is null || EmptyText is null || RebirthBox is null || RenewalBox is null) return;
        var matches = _catalog.Search(SearchBox.Text, _selectedJob, IncludeRebirth, IncludeRenewal).ToArray();
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
