using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
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
    private readonly DateTime? _restartAtUtc;
    private readonly string? _initialKillClock;
    public bool IsRestart => _restartAtUtc.HasValue;

    public bool IsDragging { get; private set; }
    public event Action? PositionChangedByUser;
    private void ShellBorder_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ShellBorder.Clip = new RectangleGeometry(new Rect(0,0,ShellBorder.ActualWidth,ShellBorder.ActualHeight),10,10);
    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;
        IsDragging = true;
        try { DragMove(); }
        finally { IsDragging = false; PositionChangedByUser?.Invoke(); }
    }

    public ICollectionView MonsterView { get; }
    public MvpTimer Draft { get; }
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

    public MvpTimerWizardWindow(IEnumerable<MonsterEntry> monsters, MvpTimer? restartSource = null, DateTime? killedAtUtc = null)
    {
        _restartAtUtc = restartSource is null ? null : killedAtUtc ?? DateTime.UtcNow;
        Draft = restartSource?.CreateRestartDraft(_restartAtUtc!.Value) ?? new MvpTimer();
        InitializeComponent();
        MonsterBox.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(MonsterBox_PreviewMouseLeftButtonDown), true);
        _monsters = new ObservableCollection<MonsterEntry>(monsters);
        if (restartSource is not null && !_monsters.Any(monster => monster.Id == restartSource.MonsterId))
            _monsters.Add(new MonsterEntry { Id = restartSource.MonsterId, Name = restartSource.Name,
                ImageUrl = restartSource.MonsterImagePath, Spawns = restartSource.RespawnHints.ToList() });
        MonsterView = CollectionViewSource.GetDefaultView(_monsters);
        DataContext = this;
        if (restartSource is not null)
        {
            UseExactKillTime = true;
            KillTimeText = _initialKillClock = _restartAtUtc!.Value.ToLocalTime().ToString("HH:mm:ss");
            _suppressMonsterTextChanged = true;
            try
            {
                SelectedMonster = _monsters.FirstOrDefault(monster => monster.Id == restartSource.MonsterId);
                MonsterBox.SelectedItem = SelectedMonster;
            }
            finally { _suppressMonsterTextChanged = false; }
            ConfirmButton.SetResourceReference(ContentControl.ContentProperty, "Restart");
            SetResourceReference(TitleProperty, "RestartMvpTimer");
        }
        Width = 800; Height = 540;
    }

    private void MonsterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMonsterTextChanged || MonsterBox.SelectedItem is not MonsterEntry monster) return;
        SelectedMonster = monster;
        if (Draft.MonsterId != monster.Id) Draft.LocationCode = "";
        Draft.MonsterId = monster.Id;
        Draft.SetMonsterDetails(monster);
        Draft.SelectFirstLocationIfMissing();
        Draft.Name = monster.Name;
        ValidationText.Text = "";
        MoveCaretToEnd();
    }

    private void MonsterBox_Loaded(object sender, RoutedEventArgs e)
    {
        MonsterBox.ApplyTemplate();
        _monsterEditor = MonsterBox.Template.FindName("PART_EditableTextBox", MonsterBox) as TextBox;
        if (_monsterEditor is not null)
        {
            _monsterEditor.TextChanged -= MonsterEditor_TextChanged;
            _monsterEditor.TextChanged += MonsterEditor_TextChanged;
            _monsterEditor.GotKeyboardFocus += (_, _) => _monsterEditor.SelectionLength = 0;
        }
    }

    private void MonsterEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressMonsterTextChanged || _monsterEditor is not { IsKeyboardFocused: true } editor) return;
        if (MonsterBox.SelectedItem is MonsterEntry selected && editor.Text == selected.DisplayName) return;
        var text = editor.Text;
        var caret = editor.CaretIndex;
        var query = text.Trim();
        _suppressMonsterTextChanged = true;
        try
        {
            MonsterView.Filter = value => value is MonsterEntry monster &&
                (string.IsNullOrWhiteSpace(query) ||
                 monster.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                 monster.NameRu.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                 monster.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
            editor.Text = text;
            editor.CaretIndex = Math.Min(caret, text.Length);
            MonsterBox.IsDropDownOpen = !MonsterView.IsEmpty;
        }
        finally { _suppressMonsterTextChanged = false; }
    }

    private void MonsterBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_monsterEditor is null) return;
        var source=e.OriginalSource as DependencyObject;
        for(var node=source;node is not null && node!=MonsterBox;)
        {
            if(node is ToggleButton or ComboBoxItem)return;
            node=node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        var point=e.GetPosition(_monsterEditor);
        _monsterEditor.Focus();
        var caret=_monsterEditor.GetCharacterIndexFromPoint(point,true);
        _monsterEditor.Select(caret<0?_monsterEditor.Text.Length:caret,0);
        MonsterBox.IsDropDownOpen=true;
        e.Handled=true;
    }

    private void MonsterBox_DropDownClosed(object sender, EventArgs e)
    {
        if (_monsterEditor is null) return;
        var draft=_monsterEditor.Text;var caret=_monsterEditor.CaretIndex;
        _suppressMonsterTextChanged=true;
        try
        {
            MonsterView.Filter=null;
            _monsterEditor.Text=draft;
            _monsterEditor.Select(Math.Clamp(caret,0,draft.Length),0);
        }
        finally{_suppressMonsterTextChanged=false;}
    }

    private void MoveCaretToEnd()
    {
        var selected=SelectedMonster;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_monsterEditor is null || selected is null || SelectedMonster!=selected ||
                _monsterEditor.Text!=selected.DisplayName) return;
            _monsterEditor.Select(_monsterEditor.Text.Length,0);
        });
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if ((!IsRestart && Draft.MonsterId <= 0) || (_monsterEditor is not null && SelectedMonster is not null &&
            _monsterEditor.Text != SelectedMonster.DisplayName))
        {
            ValidationText.Text = LocalizationService.Get("ChooseMvpRequired");
            return;
        }

        if (IsRestart && (!UseExactKillTime || KillTimeText == _initialKillClock))
        {
            // Preserve the complete keypress timestamp, including its date.
            // Waiting for a marker or crossing midnight must not shift the kill.
            Draft.StartFromKillTime(_restartAtUtc!.Value);
        }
        else if (UseExactKillTime)
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
