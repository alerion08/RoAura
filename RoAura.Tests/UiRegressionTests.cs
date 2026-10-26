using Xunit;

namespace RoAura.Tests;

public sealed class UiRegressionTests
{
    [Fact]
    public void MainWindow_KeepsRedesignedRewardWorkspace()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("MainWindow.xaml"));

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
        var xaml = File.ReadAllText(FindRepositoryFile("MainWindow.xaml"));

        Assert.Contains("<Grid Grid.Column=\"2\" DataContext=\"{Binding SelectedItem, ElementName=SkillsGrid}\"", xaml);
        Assert.Contains("<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>", xaml);
        Assert.DoesNotContain("<ScrollViewer Grid.Column=\"2\" VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Disabled\">\n                            <Grid DataContext=\"{Binding SelectedItem, ElementName=SkillsGrid}\"", xaml);
    }

    [Fact]
    public void MainWindow_KeepsRedesignedFeatureEditors()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("MainWindow.xaml"));

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
        var xaml = File.ReadAllText(FindRepositoryFile("MainWindow.xaml"));

        Assert.Contains("<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>", xaml);
        Assert.Contains("<Border Grid.Row=\"2\" Padding=\"8\" Background=\"#F5F9FE\" CornerRadius=\"7\">\n                                                    <UniformGrid Columns=\"3\">\n                                                        <Button Content=\"{DynamicResource DuplicateEffect}\"", xaml);
        Assert.Contains("<Grid Grid.Column=\"2\">\n                                        <Grid.RowDefinitions><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>", xaml);
        Assert.Contains("<Border Grid.Row=\"1\" Padding=\"8\" Background=\"#F5F9FE\" CornerRadius=\"7\">\n                                            <UniformGrid Columns=\"4\">\n                                                <ToggleButton x:Name=\"BuffPanelMoveModeButton\"", xaml);
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
}
