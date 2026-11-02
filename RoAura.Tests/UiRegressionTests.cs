using Xunit;
using System.Xml.Linq;

namespace RoAura.Tests;

public sealed class UiRegressionTests
{
    [Fact]
    public void MainWindow_KeepsRedesignedRewardWorkspace()
    {
        var xaml = ReadRepositoryText("MainWindow.xaml");

        Assert.Contains("RewardsSettingsTitle", xaml);
        Assert.Contains("StatisticsTab", xaml);
        Assert.Contains("RewardChannelsTitle", xaml);
        Assert.Contains("<ListBox Grid.Row=\"1\" x:Name=\"RewardChannelsGrid\"", xaml);
        Assert.Contains("x:Name=\"RateIndicatorsGrid\" ItemsSource=\"{Binding RateIndicators}\"", xaml);
        Assert.DoesNotContain("<DataGrid x:Name=\"RewardChannelsGrid\"", xaml);
        Assert.DoesNotContain("<DataGrid x:Name=\"RateIndicatorsGrid\"", xaml);
    }

    [Fact]
    public void MainWindow_KeepsCooldownActionsInStretchingEditor()
    {
        var xaml = ReadRepositoryText("MainWindow.xaml");

        Assert.Contains("<Grid Grid.Column=\"2\" DataContext=\"{Binding SelectedItem, ElementName=SkillsGrid}\"", xaml);
        Assert.Contains("<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>", xaml);
        Assert.DoesNotContain("<ScrollViewer Grid.Column=\"2\" VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Disabled\">\n                            <Grid DataContext=\"{Binding SelectedItem, ElementName=SkillsGrid}\"", xaml);
    }

    [Fact]
    public void MainWindow_KeepsRedesignedFeatureEditors()
    {
        var xaml = ReadRepositoryText("MainWindow.xaml");

        Assert.Contains("x:Name=\"EffectsSectionTabs\"", xaml);
        Assert.Contains("<ListBox x:Name=\"EffectsGrid\"", xaml);
        Assert.Contains("<ListBox Grid.Row=\"1\" x:Name=\"RemindersGrid\"", xaml);
        Assert.Contains("<ListBox Grid.Row=\"1\" x:Name=\"EquipmentGrid\"", xaml);
        Assert.Contains("<ListBox Grid.Row=\"1\" x:Name=\"MvpTimersGrid\"", xaml);
        Assert.Contains("x:Name=\"GoldGoblinWorkspaceTabs\"", xaml);
        Assert.DoesNotContain("<DataGrid x:Name=\"EffectsGrid\"", xaml);
        Assert.DoesNotContain("<DataGrid x:Name=\"RemindersGrid\"", xaml);
        Assert.DoesNotContain("<DataGrid x:Name=\"EquipmentGrid\"", xaml);
        Assert.DoesNotContain("<DataGrid x:Name=\"MvpTimersGrid\"", xaml);
    }

    [Fact]
    public void MainWindow_KeepsEffectActionsDockedAtTheBottom()
    {
        var xaml = ReadRepositoryText("MainWindow.xaml");

        Assert.Contains("<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>", xaml);
        AssertActionFooter(xaml,"2","3","Button","Content","{DynamicResource DuplicateEffect}");
        Assert.Contains("<Grid Grid.Column=\"2\">\n                                        <Grid.RowDefinitions><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>", xaml);
        AssertActionFooter(xaml,"1","4","ToggleButton","Name","BuffPanelMoveModeButton");
    }

    private static void AssertActionFooter(string xaml,string row,string columns,string control,string attribute,string value)
    {
        XNamespace ui="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace names="http://schemas.microsoft.com/winfx/2006/xaml";
        var document=XDocument.Parse(xaml);
        Assert.Contains(document.Descendants(ui+"Border"),border=>(string?)border.Attribute("Grid.Row")==row &&
            border.Elements(ui+"UniformGrid").Any(grid=>(string?)grid.Attribute("Columns")==columns &&
                grid.Elements(ui+control).Any(action=>(string?)action.Attribute(attribute=="Name"?names+attribute:attribute)==value)));
    }
    private static string FindRepositoryFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "RoAura.csproj")))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{fileName}'.");
    }

    private static string ReadRepositoryText(string fileName) =>
        File.ReadAllText(FindRepositoryFile(fileName)).Replace("\r\n", "\n");
}
