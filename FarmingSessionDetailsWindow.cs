using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;
namespace RoAura;

public sealed class FarmingSessionDetailsWindow : Window
{
    public FarmingSessionDetailsWindow(FarmingSession session)
    {
        Title = LocalizationService.Get("SessionLootDetails");
        Width = 850; Height = 560; MinWidth = 620; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"RoWindowBrush");
        SetResourceReference(ForegroundProperty,"RoTextBrush");
        var root=new Grid {Margin=new Thickness(16)};
        root.RowDefinitions.Add(new() {Height=GridLength.Auto});
        root.RowDefinitions.Add(new() {Height=new GridLength(1,GridUnitType.Star)});
        root.Children.Add(new TextBlock {Text=session.CharacterCaption,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,12)});
        var grid=new DataGrid {AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,ItemsSource=session.Loot};
        FarmingSessionHistoryControl.AddColumn(grid,"Item","Name");
        FarmingSessionHistoryControl.AddColumn(grid,"Count","Count");
        FarmingSessionHistoryControl.AddColumn(grid,"HistoryUnitPrice","UnitPrice");
        FarmingSessionHistoryControl.AddColumn(grid,"PriceSource","PriceSource");
        FarmingSessionHistoryControl.AddColumn(grid,"Total","Total");
        Grid.SetRow(grid,1);root.Children.Add(grid);Content=root;
    }
}
