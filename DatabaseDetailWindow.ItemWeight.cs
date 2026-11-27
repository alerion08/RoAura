using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using RoAura.Models;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    private void AddItemWeight(DatabaseItem item, SpringRoItemDetails? detail)
    {
        var weightText = item.Stats.GetValueOrDefault("Weight");
        if (!TryParseWeight(weightText, out var weight))
        {
            var description = string.IsNullOrWhiteSpace(detail?.Description) ? item.Description : detail.Description;
            if (!TryParseWeight(DescriptionWeight().Match(description).Groups[1].Value, out weight))
                return;
        }

        var view = _itemCardView;
        var label = Meta(T($"Вес за ед. {weight:0.##} · Кол-во", $"Unit weight {weight:0.##} · Qty"));
        var quantity = new TextBox
        {
            Text = view?.WeightQuantity ?? "1",
            Width = 74,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        System.Windows.Automation.AutomationProperties.SetLabeledBy(quantity, label);
        var total = Meta("");
        quantity.TextChanged += (_, _) =>
        {
            if (view is not null) view.WeightQuantity = quantity.Text;
            UpdateTotal();
        };
        UpdateTotal();

        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        label.VerticalAlignment = VerticalAlignment.Center;
        total.VerticalAlignment = VerticalAlignment.Center;
        total.Margin = new Thickness(7, 0, 0, 0);
        row.Children.Add(label);
        row.Children.Add(quantity);
        row.Children.Add(total);
        DetailsPanel.Children.Add(row);

        void UpdateTotal()
        {
            if (int.TryParse(quantity.Text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var count) &&
                count > 0 && (weight == 0 || weight <= decimal.MaxValue / count))
            {
                total.Text = T("Общий вес", "Total weight") + $" · {weight * count:0.############################}";
                return;
            }

            total.Text = T("Введите целое количество от 1 до 2 147 483 647.",
                "Enter a whole quantity from 1 to 2,147,483,647.");
        }
    }

    private static bool TryParseWeight(string? value, out decimal weight) =>
        decimal.TryParse(value?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out weight);

    [GeneratedRegex(@"^\s*(?:Weight|Вес)[ \t]*:[ \t]*([0-9]+(?:[.,][0-9]+)?)[ \t\r]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionWeight();
}
