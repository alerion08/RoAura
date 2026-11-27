using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseOverlayWindow
{
    private void UpdateDatabasePalette() =>
        AppearanceService.ApplyPalette(this, AppearanceService.Resolve(_settings.ApplicationTheme, _settings.GameOverlayTheme, true));

    private bool _resettingMonsterFilters;

    private void ResetMonsterFilters_Click(object sender, RoutedEventArgs e)
    {
        _resettingMonsterFilters = true;
        try
        {
            RaceFilter.SelectedIndex = ElementFilter.SelectedIndex = SizeFilter.SelectedIndex = 0;
            AggressiveFilter.IsChecked = BossFilter.IsChecked = MvpFilter.IsChecked = false;
            foreach (var field in new[] { ElementLevelMinFilter, LevelMinFilter, LevelMaxFilter,
                         BaseExpMinFilter, JobExpMinFilter, HpMinFilter, HpMaxFilter, DropItemFilter })
                field.Clear();
        }
        finally { _resettingMonsterFilters = false; }
        RefreshResults();
    }

    private void ResultDecoration_Loaded(object sender, RoutedEventArgs e) => UpdateResultIcon((ContentControl)sender);
    private void ResultDecoration_Changed(object sender, DependencyPropertyChangedEventArgs e) => UpdateResultIcon((ContentControl)sender);

    private void UpdateResultIcon(ContentControl control)
    {
        if (control.DataContext is not DatabaseSearchResult result) { control.Content = null; return; }
        var (bytes, glyph) = result.Value switch
        {
            DatabaseMonster monster => (monster.LocalImagePng, "♞"),
            DatabaseItem item => (item.LocalImagePng, "◇"),
            DatabaseMap map => (map.LocalImagePng, "▧"),
            RagnarokPlusBarterCatalog.Offer offer => (_database.Item(offer.ItemId ?? 0)?.LocalImagePng, "⇄"),
            MarketListing listing => (_database.Item(listing.ItemId)?.LocalImagePng, "◇"),
            _ => (null, "◇")
        };
        control.Content = DatabasePresentation.Icon(bytes, glyph, 18);
    }

    private void ResultMetric_Loaded(object sender, RoutedEventArgs e) => UpdateResultMetric((TextBlock)sender);
    private void ResultMetric_Changed(object sender, DependencyPropertyChangedEventArgs e) => UpdateResultMetric((TextBlock)sender);

    private void UpdateResultMetric(TextBlock label)
    {
        label.Text = (label.DataContext as DatabaseSearchResult)?.Value switch
        {
            DatabaseMonster monster when !string.IsNullOrWhiteSpace(monster.Level) => $"Lv {monster.Level}",
            MarketListing listing => MarketPriceCaption(listing.Price),
            RagnarokPlusBarterCatalog.Offer offer => offer.Quantity is long quantity ? $"×{quantity:N0}" : "—",
            _ => ""
        };
        label.Visibility = label.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
