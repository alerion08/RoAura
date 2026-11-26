using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseOverlayWindow
{
    private readonly Dictionary<(string Server, bool Buying, int ItemId, string Mode, string Refine, string Cards), long>
        _bestPriceObservations = [];

    private void ClearMarketPriceDepth()
    {
        MarketPriceDepthPanel.Visibility = Visibility.Collapsed;
        MarketPriceDepthRows.Children.Clear();
    }

    private void ShowMarketPriceDepth(IReadOnlyList<MarketListing> listings, bool buying, IReadOnlyList<MarketListing>? previousListings = null)
    {
        var variants = MarketPriceDepth.Build(listings, buying);
        if (variants.Length == 0) return;

        string BestPrice(MarketPriceDepth.PriceLevel level) => buying
            ? T($"Лучшая скупка: {level.Price:N0} z · требуется {level.Quantity:N0} шт.",
                $"Best buy offer: {level.Price:N0} z · {level.Quantity:N0} wanted")
            : T($"Лучшая продажа: {level.Price:N0} z · в наличии {level.Quantity:N0} шт.",
                $"Best sell offer: {level.Price:N0} z · {level.Quantity:N0} available");

        MarketPriceDepthHeader.Text = variants.Length == 1
            ? BestPrice(variants[0].Levels[0])
            : T("Остатки по ценам", "Quantity by price");
        var previousVariants = previousListings is null ? null : MarketPriceDepth.Build(previousListings, buying)
            .GroupBy(variant => variant.Key).ToDictionary(group => group.Key, group => group.First().Levels[0].Price);
        foreach (var variant in variants)
        {
            var label = string.Join(" · ", new[] { variant.ItemName, variant.GameMode, variant.Refine, variant.Cards }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            MarketPriceDepthRows.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 2)
            });
            var best = variant.Levels[0].Price;
            var key = (_settings.ServerProfile, buying, variant.ItemId, variant.GameMode, variant.Refine, variant.Cards);
            var previous = previousVariants is not null
                ? previousVariants.GetValueOrDefault(variant.Key)
                : _bestPriceObservations.GetValueOrDefault(key);
            _bestPriceObservations[key] = best;
            if (previous > 0)
            {
                var delta = best - previous;
                MarketPriceDepthRows.Children.Add(new TextBlock
                {
                    Text = delta == 0 ? T("Лучшая цена без изменений", "Best price unchanged") :
                        T($"К прошлому наблюдению: {delta:+#,0;-#,0} z (было {previous:N0} z)",
                          $"Since previous observation: {delta:+#,0;-#,0} z (was {previous:N0} z)"),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            foreach (var level in variant.Levels)
            {
                MarketPriceDepthRows.Children.Add(new TextBlock
                {
                    Text = variants.Length > 1 && level == variant.Levels[0] ? BestPrice(level) :
                        T($"{level.Price:N0} z · {level.Quantity:N0} шт.", $"{level.Price:N0} z · {level.Quantity:N0} pcs"),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }
        MarketPriceDepthPanel.Visibility = Visibility.Visible;
    }
}
