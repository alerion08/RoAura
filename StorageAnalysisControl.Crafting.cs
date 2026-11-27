using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Polyline = System.Windows.Shapes.Polyline;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public sealed partial class StorageAnalysisControl
{
    private sealed record HistoryPoint(DateTimeOffset At, Dictionary<string, Dictionary<int, long>> Counts);
    private static string HistoryPath => Path.Combine(AppEnvironment.LocalDirectory, "storage-history.ragnarokplus.json");
    private HistoryPoint[] _history = [];
    private Dictionary<int, long> _marketAsks = [];

    private void ReadHistory()
    {
        try
        {
            if (File.Exists(HistoryPath))
                _history = JsonSerializer.Deserialize<HistoryPoint[]>(File.ReadAllText(HistoryPath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DebugLog.Write($"RAGNAROK+ STORAGE HISTORY ERROR: {ex.GetType().Name}");
        }
    }

    private void RecordSnapshot(StorageAnalysis.Entry[] entries)
    {
        var point = new HistoryPoint(DateTimeOffset.UtcNow, entries.GroupBy(e => e.Mode).ToDictionary(
            mode => mode.Key, mode => mode.GroupBy(e => e.ItemId).ToDictionary(group => group.Key, group => group.Sum(e => e.Amount))));
        _history = _history.Append(point).TakeLast(60).ToArray();
        UpdateAge();
        var temp = HistoryPath + ".tmp";
        try
        {
            Directory.CreateDirectory(AppEnvironment.LocalDirectory);
            File.WriteAllText(temp, JsonSerializer.Serialize(_history));
            File.Move(temp, HistoryPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"RAGNAROK+ STORAGE HISTORY SAVE ERROR: {ex.GetType().Name}");
        }
    }

    private void UpdateAge()
    {
        if (_history.LastOrDefault() is not { } point)
        {
            _lastImport.Text = T("Данных ещё нет", "No import yet");
            return;
        }
        var age = DateTimeOffset.UtcNow - point.At;
        _lastImport.Text = T($"Обновлено {point.At.ToLocalTime():g} · {Math.Max(0, age.TotalMinutes):N0} мин назад",
            $"Updated {point.At.ToLocalTime():g} · {Math.Max(0, age.TotalMinutes):N0} min ago");
    }

    private void ShowHistory(Item item)
    {
        var mode = _mode.SelectedIndex == 1 ? "HC" : "SC";
        var samples = _history.Select(point => (point.At, Count: point.Counts.GetValueOrDefault(mode)?.GetValueOrDefault(item.Id) ?? 0)).ToArray();
        if (samples.Length < 2) return;
        Text(_details, T("История запаса", "Stock history"), true);
        var max = Math.Max(1, samples.Max(point => point.Count));
        var range = Math.Max(1, (samples[^1].At - samples[0].At).TotalSeconds);
        var line = new Polyline
        {
            Stretch = Stretch.Fill, Height = 56, StrokeThickness = 2, Margin = new Thickness(1, 4, 1, 4),
            Points = new PointCollection(samples.Select(point =>
                new Point((point.At - samples[0].At).TotalSeconds / range, 1 - (double)point.Count / max)))
        };
        line.SetResourceReference(Polyline.StrokeProperty, "RoBlueBrush");
        var min = samples.Min(point => point.Count);
        if (min == samples.Max(point => point.Count))
        {
            var flat = new Border { Height = 2, Margin = new Thickness(0, 20, 0, 20) };
            flat.SetResourceReference(Border.BackgroundProperty, "RoBlueBrush");
            _details.Children.Add(flat);
        }
        else _details.Children.Add(line);
        Text(_details, $"{samples[0].At.ToLocalTime():d} — {samples[^1].At.ToLocalTime():d} · {min:N0}–{max:N0}");
    }

    private DockPanel CraftWorkspace()
    {
        var dock = new DockPanel();
        var tools = new WrapPanel();
        _craftGroup.ItemsSource = new[] { T("Все рецепты", "All recipes"), T("Экипировка", "Equipment"),
            T("Расходники", "Usable"), T("Обмены", "Barter"), T("Прочее", "Other") };
        _craftGroup.SelectedIndex = 0;
        _threshold.ToolTip = T("Минимальная готовность в процентах", "Minimum readiness percentage");
        tools.Children.Add(_craftGroup);
        tools.Children.Add(new TextBlock { Text = T("Готовность от, %", "Minimum readiness, %"), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center });
        tools.Children.Add(_threshold);
        DockTop(dock, tools);
        dock.Children.Add(Split(_craft, _craftDetails));
        _craftGroup.SelectionChanged += (_, _) => RefreshCrafts();
        _threshold.TextChanged += (_, _) =>
        {
            if (!int.TryParse(_threshold.Text, out var threshold) || threshold is < 0 or > 100) return;
            if (Preferences is { } settings) { settings.StorageCraftThreshold = threshold; SavePreferences(); }
            RefreshCrafts();
        };
        return dock;
    }

    private void RefreshCrafts()
    {
        if (_resolveCost is null) return;
        var threshold = int.TryParse(_threshold.Text, out var value) ? Math.Clamp(value, 0, 100) : 60;
        var selected = (_craft.SelectedItem as StorageAnalysis.Craft)?.Target;
        _craft.ItemsSource = StorageAnalysis.Plans(_summary.ToDictionary(item => item.Id, item => item.Count), _resolveCost, threshold)
            .Where(craft => _craftGroup.SelectedIndex <= 0 || _craftGroup.SelectedIndex == CraftGroup(craft)).ToArray();
        _craft.SelectedItem = _craft.Items.OfType<StorageAnalysis.Craft>().FirstOrDefault(craft => Equals(craft.Target, selected));
    }

    private int CraftGroup(StorageAnalysis.Craft craft)
    {
        if (craft.Target is RagnarokPlusBarterCatalog.Offer) return 3;
        return (craft.Target is int id ? _database?.Item(id)?.Type : null)?.ToLowerInvariant() switch
        {
            "armor" or "weapon" or "shadowgear" or "equipment" or "costume" => 1,
            "healing" or "usable" or "delayconsume" or "consumable" => 2,
            _ => 4
        };
    }

    private void ShowWithdrawals(StorageAnalysis.Material part)
    {
        var remaining = part.Required;
        foreach (var slot in (_summary.FirstOrDefault(item => item.Id == part.ItemId)?.Entries ?? []).GroupBy(entry => entry.Slot).OrderBy(group => group.Key))
        {
            var take = Math.Min(remaining, slot.Sum(entry => entry.Amount));
            if (take <= 0) break;
            remaining -= take;
            Text(_craftDetails, T($"Забрать из хранилища {slot.Key} · {take:N0}", $"Withdraw from storage {slot.Key} · {take:N0}"));
        }
    }

    private void ShowCraftEconomics(StorageAnalysis.Craft craft)
    {
        var fee = craft.Target is RagnarokPlusBarterCatalog.Offer offer ? BarterFee(offer) : 0m;
        var full = SumCosts(craft.Materials.Select(part => ComponentCost(part.ItemId, part.Required, [])), fee);
        var missing = SumCosts(craft.Materials.Select(part => PurchaseCost(part.ItemId, part.Missing)), 0);
        Text(_craftDetails, T($"Себестоимость · {Money(full)}", $"Production cost · {Money(full)}"));
        Text(_craftDetails, T($"Докупка остатка · {Money(missing)}", $"Buy remaining materials · {Money(missing)}"));
        foreach (var part in craft.Materials.Where(part => part.Missing > 0))
        {
            var buy = PurchaseCost(part.ItemId, part.Missing);
            var make = MakeCost(part.ItemId, part.Missing, [part.ItemId], 0);
            if (make is not null)
                Text(_craftDetails, T($"{part.Name} · купить {Money(buy)} · изготовить {Money(make)}",
                    $"{part.Name} · buy {Money(buy)} · make {Money(make)}"));
        }

        if (Preferences is not { } settings || !craft.Materials.Any(part => part.Missing > 0)) return;
        var lists = LootLists.ForServer(settings, GameServerProfile.RagnarokPlus).ToArray();
        if (lists.Length == 0) return;
        var choice = new ComboBox { ItemsSource = lists, SelectedIndex = 0 };
        _craftDetails.Children.Add(choice);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _craftDetails.Children.Add(Action(T("Добавить дефицит в список добычи", "Add shortages to loot list"), () =>
        {
            if (choice.SelectedItem is not LootList list) return;
            foreach (var part in craft.Materials.Where(part => part.Missing > 0))
            {
                LootLists.AddItem(list, part.ItemId, part.Name);
                var item = list.Items.First(item => item.ItemId == part.ItemId);
                item.Target = Math.Max(item.Target, (long)Math.Min(int.MaxValue, item.Acquired + (decimal)part.Missing));
            }
            SavePreferences();
            (Application.Current as App)?.LootListsChanged();
            status.Text = T("Дефицит добавлен", "Shortages added");
        }));
        _craftDetails.Children.Add(status);
    }

    private decimal? PurchaseCost(int id, long amount) => amount == 0 ? 0 :
        _marketAsks.TryGetValue(id, out var ask) ? amount * (decimal)ask : null;

    private decimal? ComponentCost(int id, long amount, HashSet<int> path)
    {
        var buy = PurchaseCost(id, amount);
        if (!path.Add(id)) return buy;
        var make = MakeCost(id, amount, path, path.Count);
        path.Remove(id);
        return buy is null ? make : make is null ? buy : Math.Min(buy.Value, make.Value);
    }

    private decimal? MakeCost(int id, long amount, HashSet<int> path, int depth)
    {
        if (depth >= 5) return null;
        var costs = new List<decimal>();
        foreach (var recipe in RagnarokPlusRecipes.All.Where(recipe => recipe.ItemId == id && recipe.Amount > 0))
        {
            var repeats = (long)Math.Ceiling((decimal)amount / recipe.Amount);
            var materials = recipe.Materials.Where(part => !part.IsGuide && part.Amount > 0).ToArray();
            if (materials.Length == 0) continue;
            if (SumCosts(materials.Select(part => ComponentCost(part.ItemId, Scaled(part.Amount, repeats), path)), 0) is { } cost)
                costs.Add(cost);
        }
        if (_resolveCost is not null)
        {
            foreach (var offer in RagnarokPlusBarterCatalog.All.Where(offer => offer.ItemId == id && offer.Quantity is > 0))
            {
                var repeats = (long)Math.Ceiling((decimal)amount / offer.Quantity!.Value);
                var materials = offer.Costs.Where(part => part.Kind == "item").ToArray();
                if (materials.Length == 0 || materials.Any(part => part.Quantity is not > 0 || !_resolveCost(part).HasValue)) continue;
                if (SumCosts(materials.Select(part => ComponentCost(_resolveCost(part)!.Value, Scaled(part.Quantity!.Value, repeats), path)),
                    BarterFee(offer) * repeats) is { } cost)
                    costs.Add(cost);
            }
        }
        return costs.Count == 0 ? null : costs.Min();
    }

    private static long Scaled(long a, long b) => (long)(b > long.MaxValue / a ? long.MaxValue : a * b);
    private static decimal BarterFee(RagnarokPlusBarterCatalog.Offer offer) =>
        offer.Costs.Where(cost => cost.Kind == "zeny").Sum(cost => (decimal)(cost.Quantity ?? 0));

    private static decimal? SumCosts(IEnumerable<decimal?> values, decimal fee)
    {
        foreach (var value in values)
        {
            if (value is null || decimal.MaxValue - fee < value.Value) return null;
            fee += value.Value;
        }
        return fee;
    }

    private string Money(decimal? price) => price.HasValue ? $"{price:N0} z" : T("цена неизвестна", "price unknown");
}
