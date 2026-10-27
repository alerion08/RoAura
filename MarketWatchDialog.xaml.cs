using System.Windows;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MarketWatchDialog : Window
{
    public MarketWatchRule? Rule { get; private set; }
    public MarketWatchDialog(GameDatabaseService database, DatabaseItem? item = null, MarketWatchRule? rule = null)
    {
        InitializeComponent();
        Editor.Initialize(database, item, rule);
        Loaded += (_, _) => Editor.FocusEditor();
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.CreateRule() is not { } rule) return;
        Rule = rule;
        DialogResult = true;
    }
}
