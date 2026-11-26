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
    private TextBox? _jobEditor;
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

    private void JobBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (_jobEditor is not null) _jobEditor.TextChanged -= JobEditor_TextChanged;
        JobBox.ApplyTemplate();
        _jobEditor = JobBox.Template.FindName("PART_EditableTextBox", JobBox) as TextBox;
        if (_jobEditor is not null) _jobEditor.TextChanged += JobEditor_TextChanged;
    }

    private void JobEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingJobs || _jobEditor is not { IsKeyboardFocused: true } editor ||
            JobBox.SelectedItem is string selected && selected == editor.Text) return;
        var text = editor.Text;
        var caret = editor.CaretIndex;
        _selectedJob = null;
        UpdateJobs(text);
        _updatingJobs = true;
        try
        {
            editor.Text = text;
            editor.CaretIndex = Math.Min(caret, text.Length);
            JobBox.IsDropDownOpen = JobBox.HasItems;
        }
        finally { _updatingJobs = false; }
    }

    private void JobBox_DropDownOpened(object sender, EventArgs e)
    {
        if (!_updatingJobs) UpdateJobs();
    }

    private void JobBox_DropDownClosed(object sender, EventArgs e)
    {
        if (!_updatingJobs) UpdateJobs();
    }

    private void JobBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingJobs || JobBox.SelectedItem is not string name) return;
        _selectedJob = _catalog.Jobs.FirstOrDefault(job => job.Name == name);
        RefreshResults();
    }

    private void JobEditionChanged(object sender, RoutedEventArgs e) => UpdateJobs();

    private void UpdateJobs(string? query = null)
    {
        var jobs = _catalog.FindJobs(query ?? "", IncludeRebirth, IncludeRenewal).ToArray();
        _updatingJobs = true;
        try
        {
            var names = jobs.Select(job => job.Name);
            JobBox.ItemsSource = (string.IsNullOrWhiteSpace(query) ? names.Prepend(AllJobs) : names).ToArray();
            if (query is null)
            {
                if (_selectedJob is not null && !jobs.Contains(_selectedJob)) _selectedJob = null;
                JobBox.SelectedItem = _selectedJob?.Name ?? AllJobs;
                JobBox.Text = _selectedJob?.Name ?? AllJobs;
            }
        }
        finally { _updatingJobs = false; }
        RefreshResults();
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
