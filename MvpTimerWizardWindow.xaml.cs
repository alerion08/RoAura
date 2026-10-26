using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MvpTimerWizardWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<MonsterEntry> _monsters;
    private bool _startAfterCreate;

    public ICollectionView MonsterView { get; }
    public MvpTimer Draft { get; } = new();
    public MvpTimer? CreatedTimer { get; private set; }
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
        Draft.MonsterId = monster.Id;
        Draft.Name = monster.Name;
        ValidationText.Text = "";
    }

    private void MonsterBox_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Enter or Key.Escape or Key.Tab) return;
        if (!MonsterBox.IsDropDownOpen) MonsterBox.IsDropDownOpen = true;
        var query = MonsterBox.Text.Trim();
        MonsterView.Filter = value => value is MonsterEntry monster &&
            (string.IsNullOrWhiteSpace(query) ||
             monster.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             monster.NameRu.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             monster.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
        MonsterView.Refresh();
    }

    private void MonsterBox_DropDownClosed(object sender, EventArgs e)
    {
        MonsterView.Filter = null;
        MonsterView.Refresh();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (Draft.MonsterId <= 0)
        {
            ValidationText.Text = LocalizationService.Get("ChooseMvpRequired");
            return;
        }

        if (StartAfterCreate) Draft.Restart();
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
