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

    private void ShowHistory(Item item, Panel history)
    {
        var mode = _mode.SelectedIndex == 1 ? "HC" : "SC";
        var samples = _history.Select(point => (point.At, Count: point.Counts.GetValueOrDefault(mode)?.GetValueOrDefault(item.Id) ?? 0)).ToArray();
        if (samples.Length < 2) return;

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
            history.Children.Add(flat);
        }
        else history.Children.Add(line);
        Text(history, $"{samples[0].At.ToLocalTime():d} — {samples[^1].At.ToLocalTime():d} · {min:N0}–{max:N0}");
    }

    private readonly TextBox _craftSearch = new() { MinWidth = 145, Margin = new Thickness(4) };
    private readonly TextBlock _craftCount = new() { Margin = new Thickness(6) };

    private sealed record CraftDisplayRow(string Title, string Readiness, string Units, string Output, string Cost,
        StorageAnalysis.Craft? Plan, string? Group = null);
    private readonly Dictionary<string, bool> _expandedCraftResults = [];
    private readonly ComboBox _craftCostumes = new();

    private DockPanel CraftWorkspace()
    {
        var dock = new DockPanel();
        var tools = new WrapPanel { Margin = new Thickness(0, 2, 0, 7) };
        _craftGroup.ItemsSource = new[] { T("Все рецепты", "All recipes"), T("Экипировка", "Equipment"),
            T("Расходники", "Usable"), T("Обмены", "Barter"), T("Прочее", "Other") };
        _craftGroup.SelectedIndex = 0;
        _threshold.ToolTip = T("Минимальная готовность в процентах", "Minimum readiness percentage");
        _craftSearch.ToolTip = T("Поиск по результату или материалу", "Search by result or material");
        tools.Children.Add(_craftSearch);
        _craftSearch.TextChanged += (_, _) => RefreshCrafts();
        tools.Children.Add(_craftGroup);
        tools.Children.Add(new TextBlock { Text = T("Собрано ингредиентов, %", "Ingredients collected, %"), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center });
        tools.Children.Add(_threshold);
        _craftCostumes.ItemsSource = new[] { T("Все предметы", "All items"), T("Только костюмы", "Only costumes"),
            T("Без костюмов", "Exclude costumes") };
        _craftCostumes.SelectedIndex = 0;
        _craftCostumes.SelectionChanged += (_, _) => RefreshCrafts();
        tools.Children.Add(_craftCostumes);
        AlignFields(_craftSearch, _craftGroup, _threshold, _craftCostumes);
        var list = new DockPanel();
        DockTop(list, tools);
        ConfigureCompactTable(_craft);
        _craft.Columns.Add(PaddedColumn(T("Результат", "Result"), nameof(CraftDisplayRow.Title),
            new DataGridLength(1, DataGridLengthUnitType.Star)));
        _craft.Columns.Add(PaddedColumn(T("Собрано ингр-в", "Ingredients collected"), nameof(CraftDisplayRow.Readiness), DataGridLength.Auto));
        _craft.Columns.Add(PaddedColumn(T("Можно изготовить, шт.", "Craftable, units"), nameof(CraftDisplayRow.Units), DataGridLength.Auto));
        _craft.Columns.Add(PaddedColumn(T("Выход / оп.", "Output / op."), nameof(CraftDisplayRow.Output), DataGridLength.Auto));
        _craft.Columns.Add(PaddedColumn(T("Стоимость", "Cost"), nameof(CraftDisplayRow.Cost), DataGridLength.Auto));
        _craft.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject origin &&
                ItemsControl.ContainerFromElement(_craft, origin) is DataGridRow { Item: CraftDisplayRow { Group: { } group } })
                ToggleCraftGroup(group);
        };
        _craft.PreviewKeyDown += (_, e) =>
        {
            if ((e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space) &&
                _craft.SelectedItem is CraftDisplayRow { Group: { } group })
            {
                ToggleCraftGroup(group);
                e.Handled = true;
            }
        };
        DockPanel.SetDock(_craftCount, Dock.Bottom);
        list.Children.Add(_craftCount);
        list.Children.Add(_craft);
        dock.Children.Add(Split(list, _craftDetails, _craft));
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
        var selected = (_craft.SelectedItem as CraftDisplayRow)?.Plan;
        var selectedGroup = (_craft.SelectedItem as CraftDisplayRow)?.Group;
        var query = _craftSearch.Text.Trim();
        var storedCostumes = _entries.Where(entry => string.Equals(entry.Type, "Costume", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.ItemId).ToHashSet();
        var plans = StorageAnalysis.Plans(_summary.ToDictionary(item => item.Id, item => item.Count), _resolveCost, threshold)
            .Where(craft => (_craftGroup.SelectedIndex <= 0 || _craftGroup.SelectedIndex == CraftGroup(craft)) &&
                (_craftCostumes.SelectedIndex switch { 1 => IsCostume(craft, storedCostumes), 2 => !IsCostume(craft, storedCostumes), _ => true }) &&
                (craft.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                 craft.Materials.Any(material => material.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))))
            .ToArray();
        var results = plans.GroupBy(craft => craft.ResultId is { } id ? $"#{id}" : craft.Name).ToArray();
        var rows = new List<CraftDisplayRow>();
        foreach (var group in results)
        {
            var variants = group.ToArray();
            if (variants.Length == 1)
            {
                rows.Add(CraftRow(variants[0], false));
                continue;
            }
            var expanded = _expandedCraftResults.GetValueOrDefault(group.Key, query.Length > 0);
            var arrow = expanded ? "▾" : "▸";
            rows.Add(new CraftDisplayRow(T($"{arrow} {variants[0].Name} · {variants.Length} вариантов",
                $"{arrow} {variants[0].Name} · {variants.Length} variants"), $"{variants.Max(craft => craft.Percent)}%", "—", "—", "—", null, group.Key));
            if (expanded) rows.AddRange(variants.Select(variant => CraftRow(variant, true)));
        }
        _craft.ItemsSource = rows;
        _craft.SelectedItem = rows.FirstOrDefault(row => selectedGroup is not null
            ? row.Group == selectedGroup
            : selected is not null && row.Plan is { } plan && Equals(plan.Target, selected.Target) && plan.RecipeId == selected.RecipeId);
        _craftCount.Text = plans.Length == 0
            ? T("Нет рецептов по этим условиям.", "No recipes match these filters.")
            : T($"Результатов: {results.Length:N0} · рецептов: {plans.Length:N0}", $"Results: {results.Length:N0} · recipes: {plans.Length:N0}");
    }

    private void ToggleCraftGroup(string group)
    {
        _expandedCraftResults[group] = !_expandedCraftResults.GetValueOrDefault(group, _craftSearch.Text.Trim().Length > 0);
        RefreshCrafts();
        if (_craft.SelectedItem is { } selected) _craft.ScrollIntoView(selected);
    }

    private bool IsCostume(StorageAnalysis.Craft craft, HashSet<int> storedCostumes)
    {
        if (StorageAnalysis.HasCostumePrefix(craft.Name)) return true;
        if (craft.ResultId is not { } id) return false;
        if (storedCostumes.Contains(id)) return true;
        if (_database?.Item(id) is not { } item) return false;
        return StorageAnalysis.HasCostumePrefix(item.Name) || item.Type.Contains("costume", StringComparison.OrdinalIgnoreCase) ||
               item.AegisName.StartsWith("C_", StringComparison.OrdinalIgnoreCase) ||
               item.Stats.Any(stat => stat.Key.StartsWith("Costume_", StringComparison.OrdinalIgnoreCase) ||
                                      stat.Value.Contains("costume", StringComparison.OrdinalIgnoreCase));
    }

    private CraftDisplayRow CraftRow(StorageAnalysis.Craft craft, bool variant)
    {
        var title = variant ? "    " + string.Join(" + ", craft.Materials.Select(part => $"{part.Name} ×{part.Required:N0}")) : craft.Name;
        var fee = craft.Target is RagnarokPlusBarterCatalog.Offer offer ? BarterFee(offer) : 0m;
        var cost = craft.Target is RagnarokPlusBarterCatalog.Offer barter &&
            barter.Costs.Any(part => part.Kind != "item" && (part.Kind != "zeny" || part.Quantity is null))
            ? null : SumCosts(craft.Materials.Select(part => PurchaseCost(part.ItemId, part.Required)), fee);
        var units = craft.Output is > 0 ? Scaled(craft.Output.Value, craft.Repeats) : (long?)null;
        return new CraftDisplayRow(title, $"{craft.Percent}%", units is { } count ? $"{count:N0}" : "—",
            craft.Output is > 0 ? $"{craft.Output:N0}" : "—", cost.HasValue ? Money(cost) : "—", craft);
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
        if (fee > 0) Text(_craftDetails, T($"Плата NPC · {Money(fee)}", $"NPC fee · {Money(fee)}"));
        foreach (var part in craft.Materials.Where(part => part.Missing > 0))
        {
            var buy = PurchaseCost(part.ItemId, part.Missing);
            var make = MakeCost(part.ItemId, part.Missing, [part.ItemId], 0);
            if (make is not null)
                Text(_craftDetails, T($"{part.Name} · купить {Money(buy)} · изготовить {Money(make)}",
                    $"{part.Name} · buy {Money(buy)} · make {Money(make)}"));
        }

        if (Preferences is not { } settings || !craft.Materials.Any(part => part.Missing > 0)) return;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _craftDetails.Children.Add(Action(T("Добавить в список добычи", "Add to loot list"), () =>
        {
            var lists = LootLists.ForServer(settings, GameServerProfile.RagnarokPlus);
            var name = craft.Name;
            for (var suffix = 2; lists.Any(list => string.Equals(list.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++)
                name = $"{craft.Name} ({suffix})";
            var list = new LootList { Name = name, Server = GameServerProfile.RagnarokPlus };
            foreach (var part in craft.Materials.Where(part => part.Missing > 0))
                list.Items.Add(new LootListItem { ItemId = part.ItemId, Name = part.Name, Target = part.Missing });
            settings.LootLists.Add(list);
            SavePreferences();
            (Application.Current as App)?.LootListsChanged();
            status.Text = T($"Создан список «{name}».", $"Created “{name}”.");
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
