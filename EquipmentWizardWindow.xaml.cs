using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class EquipmentWizardWindow : Window, INotifyPropertyChanged
{
    private bool _learnAfterCreate = true;

    public EquipmentTracker Draft { get; } = new() { Name = "", Text = "" };
    public EquipmentTracker? CreatedEquipment { get; private set; }
    public bool LearnAfterCreate
    {
        get => _learnAfterCreate;
        set
        {
            if (_learnAfterCreate == value) return;
            _learnAfterCreate = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LearnAfterCreate)));
        }
    }

    public EquipmentWizardWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void PickIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PNG (*.png)|*.png|Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        Draft.IconImageBase64 = Convert.ToBase64String(File.ReadAllBytes(dialog.FileName));
        Draft.IconFileName = Path.GetFileName(dialog.FileName);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Draft.Name))
        {
            ValidationText.Text = LocalizationService.Get("EquipmentNameRequired");
            return;
        }

        ValidationText.Text = "";
        DetailsStep.Visibility = Visibility.Collapsed;
        LearnStep.Visibility = Visibility.Visible;
        StepLabel.Text = LocalizationService.Get("EquipmentWizardStep2");
        TitleLabel.Text = LocalizationService.Get("EquipmentWizardLearnTitle");
        ProgressStepTwo.Background = FindResource("RoBlueBrush") as Brush;
        BackButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        NextButton.Visibility = Visibility.Collapsed;
        CreateButton.Visibility = Visibility.Visible;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        DetailsStep.Visibility = Visibility.Visible;
        LearnStep.Visibility = Visibility.Collapsed;
        StepLabel.Text = LocalizationService.Get("EquipmentWizardStep1");
        TitleLabel.Text = LocalizationService.Get("EquipmentWizardTitle");
        ProgressStepTwo.Background = new SolidColorBrush(Color.FromRgb(216, 226, 240));
        BackButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        NextButton.Visibility = Visibility.Visible;
        CreateButton.Visibility = Visibility.Collapsed;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        CreatedEquipment = Draft;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
