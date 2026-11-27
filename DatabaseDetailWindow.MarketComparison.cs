using System.Windows;
using System.Windows.Controls;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    internal Func<DatabaseItem, string, CancellationToken, Task<IReadOnlyList<MarketListing>>>? LookupMarketSide { get; set; }

    private void AddMarketComparison(DatabaseItem item, Panel actions)
    {
        if (LookupMarketSide is not { } lookup || !GameServerProfile.SupportsMarket(_settings.ServerProfile))
            return;

        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        var refresh = new Button { Content = T("Проверить цены", "Check prices"),
            HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4, 2, 4, 2) };
        var result = new StackPanel();
        actions.Children.Add(refresh);
        panel.Children.Add(result);
        DetailsPanel.Children.Add(panel);

        CancellationTokenSource? active = null;
        panel.Unloaded += (_, _) => active?.Cancel();
        refresh.Click += async (_, _) =>
        {
            using var run = new CancellationTokenSource();
            active = run;
            refresh.IsEnabled = false;
            result.Children.Clear();
            result.Children.Add(Meta(T("Проверяем предложения…", "Checking offers…")));
            try
            {
                var sells = await lookup(item, "SELL", run.Token);
                run.Token.ThrowIfCancellationRequested();
                var buys = await lookup(item, "BUY", run.Token);
                run.Token.ThrowIfCancellationRequested();
                ShowOfferComparison(result, sells.Concat(buys).ToArray());
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested) { }
            catch (RagnarokPlusSignInRequiredException)
            {
                result.Children.Clear();
                result.Children.Add(Meta(T("Войдите в Ragnarok+ и повторите проверку.", "Sign in to Ragnarok+ and check again.")));
            }
            catch (Exception ex)
            {
                DebugLog.Write($"MARKET COMPARISON ERROR: {ex.Message}");
                result.Children.Clear();
                result.Children.Add(Meta(T("Не удалось проверить предложения.", "Could not check offers.")));
            }
            finally
            {
                active = null;
                refresh.IsEnabled = true;
            }
        };
    }

    private void ShowOfferComparison(StackPanel panel, IReadOnlyList<MarketListing> listings)
    {
        panel.Children.Clear();
        var variants = listings.GroupBy(offer => (offer.GameMode, offer.Refine, offer.Cards)).ToArray();
        if (variants.Length == 0)
        {
            panel.Children.Add(Meta(T("Предложений не найдено.", "No offers found.")));
            return;
        }

        var variant = new ComboBox { ItemsSource = variants.Select(group => string.Join(" · ",
            new[] { group.Key.GameMode, group.Key.Refine, group.Key.Cards }.Where(part => !string.IsNullOrWhiteSpace(part)))
                is { Length: > 0 } label ? label : T("Обычный предмет", "Base item")).ToArray() };
        var comparison = Meta("");
        var mode = new ComboBox
        {
            ItemsSource = new[] { T("По количеству", "By quantity"), T("По бюджету", "By budget") },
            SelectedIndex = 0
        };
        var quantityField = new StackPanel();
        var target = new TextBox { Text = "1", MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left };
        quantityField.Children.Add(Meta(T("Сколько купить", "Quantity to buy")));
        quantityField.Children.Add(target);

        var budgetField = new StackPanel { Visibility = Visibility.Collapsed };
        var budget = new TextBox { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left };
        budgetField.Children.Add(Meta(T("Бюджет, зени", "Budget, zeny")));
        budgetField.Children.Add(budget);
        var estimate = Meta("");
        panel.Children.Add(variant);
        panel.Children.Add(comparison);
        panel.Children.Add(mode);
        panel.Children.Add(quantityField);
        panel.Children.Add(budgetField);
        panel.Children.Add(estimate);
        var purchase = new StackPanel();
        panel.Children.Add(purchase);

        void Update()
        {
            if (variant.SelectedIndex < 0) return;
            purchase.Children.Clear();
            var offers = variants[variant.SelectedIndex].Select(offer =>
                (Listing: offer, Price: SpringRoMarketService.ParsePrice(offer.Price),
                    Quantity: SpringRoMarketService.ParsePrice(offer.Quantity)))
                .Where(offer => offer.Price > 0 && offer.Price < long.MaxValue &&
                    offer.Quantity > 0 && offer.Quantity < long.MaxValue).ToArray();
            var sells = offers.Where(offer => offer.Listing.ShopType == "SELL").OrderBy(offer => offer.Price).ToArray();
            var buy = offers.Where(offer => offer.Listing.ShopType == "BUY").Select(offer => offer.Price).DefaultIfEmpty().Max();
            var sell = sells.Select(offer => offer.Price).FirstOrDefault();
            string Price(long price) => price == 0 ? "—" : price.ToString("N0");
            comparison.Text = T($"Скупка {Price(buy)} · Продажа {Price(sell)}", $"Buy {Price(buy)} · Sell {Price(sell)}");
            if (buy > 0 && sell > 0)
                comparison.Text += T($" · Разница {sell - buy:N0}", $" · Spread {sell - buy:N0}");
            var byBudget = mode.SelectedIndex == 1;
            if (!long.TryParse(byBudget ? budget.Text : target.Text, out var wanted) || wanted <= 0)
            {
                estimate.Text = byBudget
                    ? T("Введите бюджет целым числом больше нуля.", "Enter a positive whole budget.")
                    : T("Введите целое количество больше нуля.", "Enter a positive whole quantity.");
                return;
            }

            long remaining = wanted;
            long purchased = 0;
            System.Numerics.BigInteger total = 0;
            foreach (var offer in sells)
            {
                var capacity = byBudget ? remaining / offer.Price : remaining;
                var take = Math.Min(capacity, offer.Quantity);
                if (take == 0) break;
                var cost = (System.Numerics.BigInteger)take * offer.Price;
                total += cost;
                purchased += take;
                remaining -= byBudget ? (long)cost : take;
                var listing = offer.Listing;
                var row = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Content = new TextBlock { TextWrapping = TextWrapping.Wrap,
                        Text = $"{take:N0} × {offer.Price:N0} · {listing.Stall} · {listing.Map} ({listing.X},{listing.Y})" }
                };
                row.Click += (_, _) => ShowDetails(listing);
                purchase.Children.Add(row);
                if (remaining == 0) break;
            }
            estimate.Text = byBudget
                ? T($"Можно купить {purchased:N0} шт. · Стоимость {total:N0} зени · Остаток {remaining:N0} зени",
                    $"Can buy {purchased:N0} · Cost {total:N0} zeny · Unspent {remaining:N0} zeny")
                : T($"Доступно {purchased:N0} из {wanted:N0} · Стоимость {total:N0} зени",
                    $"Available {purchased:N0} of {wanted:N0} · Cost {total:N0} zeny");
        }

        variant.SelectionChanged += (_, _) => Update();
        mode.SelectionChanged += (_, _) =>
        {
            var byBudget = mode.SelectedIndex == 1;
            quantityField.Visibility = byBudget ? Visibility.Collapsed : Visibility.Visible;
            budgetField.Visibility = byBudget ? Visibility.Visible : Visibility.Collapsed;
            Update();
        };
        target.TextChanged += (_, _) => Update();
        budget.TextChanged += (_, _) => Update();
        variant.SelectedIndex = 0;
    }
}
