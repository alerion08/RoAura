using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using RoAura.Models;

namespace RoAura.Controls;

/// <summary>Selectable source respawn cards; selecting a card chooses the timer map.</summary>
public sealed class MvpLocationSelector : ListBox
{
    private MvpTimer? _timer;
    private bool _refreshing;
    public MvpLocationSelector()
    {
        BorderThickness = new Thickness(0);
        Background = System.Windows.Media.Brushes.Transparent;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        ItemTemplate = (DataTemplate)XamlReader.Parse("""
<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
 <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
  <StackPanel><TextBlock Text="{Binding Name}" FontWeight="SemiBold" TextWrapping="Wrap"/>
   <TextBlock Text="{Binding Code}" Foreground="{DynamicResource RoMutedBrush}" FontSize="11"/></StackPanel>
  <TextBlock Grid.Column="1" Text="{Binding Respawn}" Foreground="{DynamicResource RoBlueBrush}" FontWeight="Bold" VerticalAlignment="Center" Margin="12,0,0,0"/>
 </Grid>
</DataTemplate>
""");
        ItemContainerStyle = (Style)XamlReader.Parse("""
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListBoxItem">
 <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
 <Setter Property="Margin" Value="0,0,0,6"/>
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem">
  <Border x:Name="Card" Padding="9,7" CornerRadius="6" Background="{DynamicResource RoCardBrush}" BorderBrush="{DynamicResource RoSoftBorderBrush}" BorderThickness="1">
   <ContentPresenter HorizontalAlignment="Stretch"/>
  </Border>
  <ControlTemplate.Triggers>
   <Trigger Property="IsSelected" Value="True"><Setter TargetName="Card" Property="Background" Value="{DynamicResource RoSelectionBrush}"/><Setter TargetName="Card" Property="BorderBrush" Value="{DynamicResource RoBlueBrush}"/><Setter TargetName="Card" Property="BorderThickness" Value="2"/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
</Style>
""");
        SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _timer is not null && SelectedItem is MonsterSpawnEntry spawn)
                _timer.LocationCode = spawn.Code;
        };
        Loaded += (_, _) => Attach();
        DataContextChanged += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }
    private void Detach() { if (_timer is not null) _timer.PropertyChanged -= Changed; _timer = null; }
    private void Attach()
    {
        Detach(); _timer = DataContext as MvpTimer;
        if (_timer is not null) _timer.PropertyChanged += Changed;
        Refresh();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(MvpTimer.RespawnHints) or nameof(MvpTimer.LocationCode)) Refresh(); }
    private void Refresh()
    {
        _refreshing = true;
        try
        {
            ItemsSource = _timer?.RespawnHints;
            SelectedItem = _timer?.RespawnHints.FirstOrDefault(spawn => spawn.Code == _timer.LocationCode);
        }
        finally { _refreshing = false; }
    }
}
