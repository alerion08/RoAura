using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class EffectWizardWindow : Window
{
    private readonly Action<TrackedEffect> _startLearning;
    private bool _manualMode;

    public TrackedEffect Draft { get; } = new();
    public ObservableCollection<PanelChoice> PanelChoices { get; } = [];
    public TrackedEffect? CreatedEffect { get; private set; }

    public EffectWizardWindow(IEnumerable<BuffPanelGroup> groups, Action<TrackedEffect> startLearning)
    {
        InitializeComponent();
        _startLearning = startLearning;
        PanelChoices.Add(new PanelChoice(0, LocalizationService.Get("WithoutPanel")));
        foreach (var group in groups.OrderBy(item => item.GroupNumber))
            PanelChoices.Add(new PanelChoice(group.GroupNumber,
                $"{LocalizationService.Get("EffectPanelGroupPrefix")} {group.GroupNumber}"));
        if (PanelChoices.Count == 1) PanelChoices.Add(new PanelChoice(1,
            $"{LocalizationService.Get("EffectPanelGroupPrefix")} 1"));
        DataContext = this;
        PanelChoiceBox.SelectedIndex = 0;
        ShowFindStep();
    }

    private void FindInGameCard_Click(object sender, MouseButtonEventArgs e) => SelectMode(false);
    private void ManualIdCard_Click(object sender, MouseButtonEventArgs e) => SelectMode(true);

    private void SelectMode(bool manual)
    {
        _manualMode = manual;
        ManualIdArea.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        FindInGameArea.Visibility = manual ? Visibility.Collapsed : Visibility.Visible;
        ManualIdCard.BorderThickness = manual ? new Thickness(2) : new Thickness(1);
        ManualIdCard.BorderBrush = manual ? FindResource("RoBlueBrush") as System.Windows.Media.Brush : FindResource("RoSoftBorderBrush") as System.Windows.Media.Brush;
        ManualIdCard.Background = manual ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 243, 255)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 251, 255));
        FindInGameCard.BorderThickness = manual ? new Thickness(1) : new Thickness(2);
        FindInGameCard.BorderBrush = manual ? FindResource("RoSoftBorderBrush") as System.Windows.Media.Brush : FindResource("RoBlueBrush") as System.Windows.Media.Brush;
        ValidationText.Text = "";
    }

    private void StartSearch_Click(object sender, RoutedEventArgs e)
    {
        Draft.StatusId = 0;
        Draft.CurrentStateText = LocalizationService.Get("WizardWaitingForEffect");
        ValidationText.Foreground = FindResource("RoBlueBrush") as System.Windows.Media.Brush;
        ValidationText.Text = LocalizationService.Get("WizardSearchRunning");
        _startLearning(Draft);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (Draft.StatusId <= 0)
        {
            ValidationText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(182, 75, 85));
            ValidationText.Text = LocalizationService.Get(_manualMode ? "WizardInvalidId" : "WizardEffectNotFound");
            return;
        }
        ShowSetupStep();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowFindStep();
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Draft.Name))
        {
            SetupValidationText.Text = LocalizationService.Get("WizardNameRequired");
            return;
        }
        SetupValidationText.Text = "";
        var choice = PanelChoiceBox.SelectedItem as PanelChoice;
        Draft.ShowOnBuffPanel = choice?.GroupNumber > 0;
        Draft.GroupNumber = choice?.GroupNumber > 0 ? choice.GroupNumber : 1;
        CreatedEffect = Draft;
        DialogResult = true;
        Close();
    }

    private void ShowFindStep()
    {
        FindStep.Visibility = Visibility.Visible;
        SetupStep.Visibility = Visibility.Collapsed;
        StepLabel.Text = LocalizationService.Get("WizardStep1");
        TitleLabel.Text = LocalizationService.Get("WizardFindTitle");
        BackButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        NextButton.Visibility = Visibility.Visible;
        CreateButton.Visibility = Visibility.Collapsed;
        ProgressStepTwo.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(216, 226, 240));
    }

    private void ShowSetupStep()
    {
        FindStep.Visibility = Visibility.Collapsed;
        SetupStep.Visibility = Visibility.Visible;
        StepLabel.Text = LocalizationService.Get("WizardStep2");
        TitleLabel.Text = LocalizationService.Get("WizardSetupTitle");
        BackButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        NextButton.Visibility = Visibility.Collapsed;
        CreateButton.Visibility = Visibility.Visible;
        ProgressStepTwo.Background = FindResource("RoBlueBrush") as System.Windows.Media.Brush;
    }

    public sealed record PanelChoice(int GroupNumber, string Label);
}
