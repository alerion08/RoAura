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
    public SkillAura Draft { get; } = new();
    private readonly Action<SkillAura>? _beginLearning;
    public event Action<SkillAura>? LearningClosed;

    public SkillWizardWindow(IEnumerable<CooldownGroupSettings> groups, bool english, Action<SkillAura>? beginLearning = null)
    {
        InitializeComponent();
        _english = english;
        _beginLearning = beginLearning;
        Draft.PropertyChanged += Draft_Changed;
        Closed += (_, _) => { Draft.PropertyChanged -= Draft_Changed; LearningClosed?.Invoke(Draft); };
        GroupBox.ItemsSource = groups.ToList();
        GroupBox.SelectedIndex = 0;
        if (_english) ApplyEnglish();
    }

    private void ApplyEnglish()
    {
        Title = "Add skill"; TitleText.Text = "Add skill"; SubtitleText.Text = "The wizard will configure a new cooldown";
        Step1Title.Text = "1. Detection method"; DetectRadio.Content = "Detect Skill ID from a game packet";
        DetectHint.Text = "The next step detects Skill ID before the cooldown is created."; ManualRadio.Content = "Enter Skill ID manually";
        Step2Title.Text = "2. Skill identifier"; SkillIdHint.Text = "Use the skill in game. Continue after its Skill ID is detected.";
        Step3Title.Text = "3. Basic settings"; NameLabel.Text = "Name"; CooldownLabel.Text = "Cooldown, seconds"; GroupLabel.Text = "Group"; ShortLabel.Text = "Short name";
        Step4Title.Text = "4. Icon and finish"; ChooseIconButton.Content = "Choose PNG"; IconFileText.Text = "No icon selected"; EnabledBox.Content = "Enable tracking immediately";
        CancelButton.Content = "Cancel"; BackButton.Content = "Back"; NextButton.Content = "Next";
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1 && DetectRadio.IsChecked == true)
        {
            _step = 2;
            RenderStep();
            BeginDetection();
            return;
        }
        if (_step == 2 && (!int.TryParse(SkillIdBox.Text, out var id) || id <= 0 || id > ushort.MaxValue))
        {
            if (DetectRadio.IsChecked == true) SkillIdHint.Text = _english ? "Use the skill in game first, or retry detection." : "Сначала примените умение в игре или повторите определение.";
            else MessageBox.Show(this, _english ? "Enter a valid Skill ID." : "Укажите корректный Skill ID.");
            return;
        }
        if (_step == 3 && (string.IsNullOrWhiteSpace(NameBox.Text) || !double.TryParse(CooldownBox.Text, out var cooldown) || cooldown < 0))
        { MessageBox.Show(this, _english ? "Enter a name and valid cooldown." : "Укажите название и корректное время восстановления."); return; }
        if (_step < 4) { _step++; RenderStep(); return; }
        Result = BuildSkill();
        DialogResult = true;
    }

    private SkillAura BuildSkill()
    {
        var selectedGroup = GroupBox.SelectedItem as CooldownGroupSettings;
        return new SkillAura
        {
            Name = NameBox.Text.Trim(), Enabled = EnabledBox.IsChecked == true,
            PacketSkillId = int.TryParse(SkillIdBox.Text, out var skillId) ? skillId : 0,
            CooldownSeconds = double.TryParse(CooldownBox.Text, out var seconds) ? seconds : 5,
            GroupNumber = selectedGroup?.GroupNumber ?? 1, ShortNameOverride = ShortBox.Text.Trim(),
            IconImageBase64 = _iconBase64, IconFileName = _iconFileName
        };
    }

    internal bool RunAutomaticFlowSmokeTest()
    {
        DetectRadio.IsChecked = true;
        _step = 1;
        Next_Click(this, new RoutedEventArgs());
        if (_step != 2 || SkillIdBox.Text.Length != 0) return false;
        Draft.PacketSkillId = 51;
        Next_Click(this, new RoutedEventArgs());
        if (_step != 3 || SkillIdBox.Text != "51") return false;
        NameBox.Text = "Smoke test skill";
        CooldownBox.Text = "5";
        Next_Click(this, new RoutedEventArgs());
        if (_step != 4) return false;
        var skill = BuildSkill();
        return skill.Name == "Smoke test skill" && skill.PacketSkillId == 51 && skill.GroupNumber > 0;
    }

    private void BeginDetection()
    {
        Draft.PacketSkillId = 0;
        SkillIdBox.Text = "";
        SkillIdHint.Text = _english ? "Use the skill in game within 20 seconds. The cooldown is not created yet." : "Примените умение в игре в течение 20 секунд. Кулдаун ещё не создан.";
        _beginLearning?.Invoke(Draft);
    }
    private void DetectAgain_Click(object sender, RoutedEventArgs e) => BeginDetection();
    private void Draft_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SkillAura.PacketSkillId) && Draft.PacketSkillId > 0)
        {
            SkillIdBox.Text = Draft.PacketSkillId.ToString();
            SkillIdHint.Text = _english ? $"Detected Skill ID {Draft.PacketSkillId}. Continue to configure the cooldown." : $"Определён Skill ID {Draft.PacketSkillId}. Можно продолжить настройку кулдауна.";
        }
        if (e.PropertyName == nameof(SkillAura.LearningObservation) && Draft.LearningObservation.Length > 0)
            SkillIdHint.Text = Draft.LearningObservation;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step <= 1) return;
        if (_step == 2) LearningClosed?.Invoke(Draft);
        _step--;
        RenderStep();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RenderStep()
    {
        Step1Panel.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = _step == 4 ? Visibility.Visible : Visibility.Collapsed;
        var bars = new[] { Step1Bar, Step2Bar, Step3Bar, Step4Bar };
        for (var index = 0; index < bars.Length; index++) bars[index].Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(index < _step ? "#3477C7" : "#DCE6F1"));
        SkillIdBox.IsReadOnly = DetectRadio.IsChecked == true;
        DetectAgainButton.Visibility = DetectRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
