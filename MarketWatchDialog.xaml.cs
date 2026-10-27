using System.Windows;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MarketWatchDialog : Window
{
    private Rect? _centerArea;
    public MarketWatchRule? Rule { get; private set; }
    public MarketWatchDialog(GameDatabaseService database, DatabaseItem? item = null, MarketWatchRule? rule = null)
    {
        InitializeComponent();
        Editor.Initialize(database, item, rule);
        Loaded += (_, _) => { Recenter(); Editor.FocusEditor(); };
        SizeChanged += (_, _) => Recenter();
    }
    public void CenterInArea(Rect area)
    {
        _centerArea = area;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Recenter();
    }
    public static Point CenterPosition(Rect area, Size size) =>
        new(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2);
    private void Recenter()
    {
        if (_centerArea is not { } area) return;
        var position = CenterPosition(area, new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : MinHeight));
        Left = position.X;
        Top = position.Y;
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.CreateRule() is not { } rule) return;
        Rule = rule;
        DialogResult = true;
    }
}
