using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MvpTimerWizardWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<MonsterEntry> _monsters;
    private bool _startAfterCreate;
    private bool _useExactKillTime;
    private string _killTimeText = DateTime.Now.ToString("HH:mm:ss");
    private MonsterEntry? _selectedMonster;
    private TextBox? _monsterEditor;
    private bool _suppressMonsterTextChanged;

    public ICollectionView MonsterView { get; }
    public MvpTimer Draft { get; } = new();
    public MvpTimer? CreatedTimer { get; private set; }
    public MonsterEntry? SelectedMonster
    {
        get => _selectedMonster;
        private set
        {
            if (ReferenceEquals(_selectedMonster, value)) return;
            _selectedMonster = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMonster)));
        }
    }
    public bool UseExactKillTime
    {
        get => _useExactKillTime;
        set
        {
            if (_useExactKillTime == value) return;
            _useExactKillTime = value;
            if (value) StartAfterCreate = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseExactKillTime)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartAfterCreate)));
        }
    }
    public bool CanStartAfterCreate => !UseExactKillTime;
    public string KillTimeText
    {
        get => _killTimeText;
        set
        {
            if (_killTimeText == value) return;
            _killTimeText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KillTimeText)));
        }
    }
    public bool StartAfterCreate
    {
        get => _startAfterCreate;
        set
        {
            if (_startAfterCreate == value) return;
            _startAfterCreate = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartAfterCreate)));
        }
    }

    public MvpTimerWizardWindow(IEnumerable<MonsterEntry> monsters)
    {
        InitializeComponent();
        _monsters = new ObservableCollection<MonsterEntry>(monsters);
        MonsterView = CollectionViewSource.GetDefaultView(_monsters);
        DataContext = this;
    }

    private void MonsterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonsterBox.SelectedItem is not MonsterEntry monster) return;
        SelectedMonster = monster;
        Draft.MonsterId = monster.Id;
        Draft.Name = monster.Name;
        ValidationText.Text = "";
        MoveCaretToEnd();
    }

    private void MonsterBox_Loaded(object sender, RoutedEventArgs e)
    {
        MonsterBox.ApplyTemplate();
        _monsterEditor = MonsterBox.Template.FindName("PART_EditableTextBox", MonsterBox) as TextBox;
        if (_monsterEditor is not null)
            _monsterEditor.TextChanged += MonsterEditor_TextChanged;
    }

    private void MonsterEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressMonsterTextChanged || _monsterEditor is null) return;
        var queryText = _monsterEditor.Text;
        var query = queryText.Trim();
        MonsterView.Filter = value => value is MonsterEntry monster &&
            (string.IsNullOrWhiteSpace(query) ||
             monster.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             monster.NameRu.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             monster.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
        MonsterView.Refresh();
        if (!MonsterBox.IsDropDownOpen) MonsterBox.IsDropDownOpen = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_monsterEditor is null) return;
            _suppressMonsterTextChanged = true;
            if (_monsterEditor.Text != queryText) _monsterEditor.Text = queryText;
            _monsterEditor.SelectionStart = _monsterEditor.Text.Length;
            _monsterEditor.SelectionLength = 0;
            _suppressMonsterTextChanged = false;
        });
    }

    private void MonsterBox_DropDownClosed(object sender, EventArgs e)
    {
        MonsterView.Filter = null;
        MonsterView.Refresh();
        MoveCaretToEnd();
    }

    private void MoveCaretToEnd()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_monsterEditor is null) return;
            _monsterEditor.SelectionStart = _monsterEditor.Text.Length;
            _monsterEditor.SelectionLength = 0;
        });
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (Draft.MonsterId <= 0)
        {
            ValidationText.Text = LocalizationService.Get("ChooseMvpRequired");
            return;
        }

        if (UseExactKillTime)
        {
            if (!TimeSpan.TryParse(KillTimeText, out var time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            {
                ValidationText.Text = LocalizationService.Get("InvalidKillTime");
                return;
            }
            Draft.StartFromKillClock(time);
        }
        else if (StartAfterCreate)
        {
            Draft.Restart();
        }
        CreatedTimer = Draft;
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
