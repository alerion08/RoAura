using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RoAura.Models;

namespace RoAura;

public partial class SkillWizardWindow : Window
{
    private readonly bool _english;
    private int _step = 1;
    private string _iconBase64 = "";
    private string _iconFileName = "";

    public SkillAura? Result { get; private set; }
    public bool LearnAfterCreate => DetectRadio.IsChecked == true;

    public SkillWizardWindow(IEnumerable<CooldownGroupSettings> groups, bool english)
    {
        InitializeComponent();
        _english = english;
        GroupBox.ItemsSource = groups.ToList();
        GroupBox.SelectedIndex = 0;
        if (_english) ApplyEnglish();
    }

    private void ApplyEnglish()
    {
        Title = "Add skill"; TitleText.Text = "Add skill"; SubtitleText.Text = "The wizard will configure a new cooldown";
        Step1Title.Text = "1. Detection method"; DetectRadio.Content = "Detect Skill ID from a game packet";
        DetectHint.Text = "After finishing the wizard, use the skill in game within 20 seconds."; ManualRadio.Content = "Enter Skill ID manually";
        Step2Title.Text = "2. Skill identifier"; SkillIdHint.Text = "In automatic mode this field may stay empty — RoAura will fill it after the skill is used.";
        Step3Title.Text = "3. Basic settings"; NameLabel.Text = "Name"; CooldownLabel.Text = "Cooldown, seconds"; GroupLabel.Text = "Group"; ShortLabel.Text = "Short name";
        Step4Title.Text = "4. Icon and finish"; ChooseIconButton.Content = "Choose PNG"; IconFileText.Text = "No icon selected"; EnabledBox.Content = "Enable tracking immediately";
        CancelButton.Content = "Cancel"; BackButton.Content = "Back"; NextButton.Content = "Next";
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 2 && ManualRadio.IsChecked == true && (!int.TryParse(SkillIdBox.Text, out var id) || id <= 0))
        { MessageBox.Show(this, _english ? "Enter a valid Skill ID." : "Укажите корректный Skill ID."); return; }
        if (_step == 3 && (string.IsNullOrWhiteSpace(NameBox.Text) || !double.TryParse(CooldownBox.Text, out var cooldown) || cooldown < 0))
        { MessageBox.Show(this, _english ? "Enter a name and valid cooldown." : "Укажите название и корректное время восстановления."); return; }
        if (_step < 4) { _step++; RenderStep(); return; }
        var selectedGroup = GroupBox.SelectedItem as CooldownGroupSettings;
        Result = new SkillAura
        {
            Name = NameBox.Text.Trim(), Enabled = EnabledBox.IsChecked == true,
            PacketSkillId = int.TryParse(SkillIdBox.Text, out var skillId) ? skillId : 0,
            CooldownSeconds = double.TryParse(CooldownBox.Text, out var seconds) ? seconds : 5,
            GroupNumber = selectedGroup?.GroupNumber ?? 1, ShortNameOverride = ShortBox.Text.Trim(),
            IconImageBase64 = _iconBase64, IconFileName = _iconFileName
        };
        DialogResult = true;
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (_step > 1) { _step--; RenderStep(); } }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RenderStep()
    {
        Step1Panel.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = _step == 4 ? Visibility.Visible : Visibility.Collapsed;
        var bars = new[] { Step1Bar, Step2Bar, Step3Bar, Step4Bar };
        for (var index = 0; index < bars.Length; index++) bars[index].Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(index < _step ? "#3477C7" : "#DCE6F1"));
        BackButton.IsEnabled = _step > 1;
        NextButton.Content = _step == 4 ? (_english ? "Create" : "Создать") : (_english ? "Next" : "Далее");
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PNG (*.png)|*.png|Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        var bytes = File.ReadAllBytes(dialog.FileName);
        _iconBase64 = Convert.ToBase64String(bytes); _iconFileName = Path.GetFileName(dialog.FileName);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = new MemoryStream(bytes); image.EndInit(); image.Freeze();
        IconPreview.Source = image; IconFileText.Text = _iconFileName;
    }
}
