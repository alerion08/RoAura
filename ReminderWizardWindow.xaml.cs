using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class ReminderWizardWindow : Window
{
    private readonly Action<ReminderItem> _startLearning;
    private bool _manualMode;

    public ReminderItem Draft { get; } = new() { Name = "", Text = "" };
    public ReminderItem? CreatedReminder { get; private set; }

    public ReminderWizardWindow(Action<ReminderItem> startLearning)
    {
        InitializeComponent();
        _startLearning = startLearning;
        DataContext = this;
        ShowFindStep();
    }

    private void FindInGameCard_Click(object sender, MouseButtonEventArgs e) => SelectMode(false);
    private void ManualIdCard_Click(object sender, MouseButtonEventArgs e) => SelectMode(true);

    private void SelectMode(bool manual)
    {
        _manualMode = manual;
        ManualIdArea.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        FindInGameArea.Visibility = manual ? Visibility.Collapsed : Visibility.Visible;
        SetCardState(ManualIdCard, manual);
        SetCardState(FindInGameCard, !manual);
        ValidationText.Text = "";
    }

    private void SetCardState(System.Windows.Controls.Border card, bool selected)
    {
        card.BorderThickness = new Thickness(selected ? 2 : 1);
        card.BorderBrush = FindResource(selected ? "RoBlueBrush" : "RoSoftBorderBrush") as Brush;
        card.Background = new SolidColorBrush(selected
            ? Color.FromRgb(234, 243, 255)
            : Color.FromRgb(248, 251, 255));
    }

    private void StartSearch_Click(object sender, RoutedEventArgs e)
    {
        Draft.StatusId = 0;
        ValidationText.Foreground = FindResource("RoBlueBrush") as Brush;
        ValidationText.Text = LocalizationService.Get("WizardSearchRunning");
        _startLearning(Draft);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (Draft.StatusId <= 0)
        {
            ValidationText.Foreground = new SolidColorBrush(Color.FromRgb(182, 75, 85));
            ValidationText.Text = LocalizationService.Get(_manualMode ? "WizardInvalidId" : "WizardEffectNotFound");
            return;
        }
        ShowSetupStep();
    }

    private void PickIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PNG (*.png)|*.png|Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        Draft.IconImageBase64 = Convert.ToBase64String(File.ReadAllBytes(dialog.FileName));
        Draft.IconFileName = Path.GetFileName(dialog.FileName);
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Draft.Name))
        {
            SetupValidationText.Text = LocalizationService.Get("ReminderNameRequired");
            return;
        }
        if (string.IsNullOrWhiteSpace(Draft.Text))
        {
            SetupValidationText.Text = LocalizationService.Get("ReminderTextRequired");
            return;
        }
        CreatedReminder = Draft;
        DialogResult = true;
        Close();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowFindStep();
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void ShowFindStep()
    {
        FindStep.Visibility = Visibility.Visible;
        SetupStep.Visibility = Visibility.Collapsed;
        StepLabel.Text = LocalizationService.Get("WizardStep1");
        TitleLabel.Text = LocalizationService.Get("ReminderWizardQuestion");
        BackButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        NextButton.Visibility = Visibility.Visible;
        CreateButton.Visibility = Visibility.Collapsed;
        ProgressStepTwo.Background = new SolidColorBrush(Color.FromRgb(216, 226, 240));
    }

    private void ShowSetupStep()
    {
        FindStep.Visibility = Visibility.Collapsed;
        SetupStep.Visibility = Visibility.Visible;
        StepLabel.Text = LocalizationService.Get("WizardStep2");
        TitleLabel.Text = LocalizationService.Get("ReminderWizardSetupTitle");
        BackButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        NextButton.Visibility = Visibility.Collapsed;
        CreateButton.Visibility = Visibility.Visible;
        ProgressStepTwo.Background = FindResource("RoBlueBrush") as Brush;
    }
}
