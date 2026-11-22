using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using RoAura.Models;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    private void AddExpandableDescription(string description)
    {
        var text = ItemDescription(description);
        DetailsPanel.Children.Add(text);
        if (description.Length <= 350 && description.Count(c => c == '\n') <= 5) return;

        text.MaxHeight = 108;
        text.ClipToBounds = true;
        var toggle = new Button { Content = T("Показать полностью", "Show full description") };
        toggle.Click += (_, _) =>
        {
            var expand = !double.IsPositiveInfinity(text.MaxHeight);
            text.MaxHeight = expand ? double.PositiveInfinity : 108;
            toggle.Content = expand ? T("Свернуть описание", "Collapse description") :
                T("Показать полностью", "Show full description");
        };
        DetailsPanel.Children.Add(toggle);
    }

    private void AddEquipmentReferences(DatabaseItem item, string description)
    {
        var slot = Regex.Match(description, @"^\s*(?:Compound on|Слот карты)\s*:\s*([^\r\n]+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (slot.Success) AddLine(T("Слот для карты", "Card equipment slot"), slot.Groups[1].Value.Trim());

        var lines = description.Split('\n').Select(line => line.Trim()).ToArray();
        var sets = new List<(string Title, string[] Names)>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("When equipped with ", StringComparison.OrdinalIgnoreCase))
            {
                var end = line.IndexOfAny([',', ':']);
                if (end < 0) end = line.Length;
                var names = line[19..end].Split(" and ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (names.Length > 0) sets.Add((line, names));
                continue;
            }
            if (!line.StartsWith('[') || !line.EndsWith(']')) continue;
            var members = lines.Skip(index + 1).TakeWhile(member => member.Length > 0 && FindEquipment(member) is not null).ToArray();
            if (members.Length >= 2 && members.Any(member => FindEquipment(member)?.Id == item.Id))
                sets.Add((line[1..^1], members));
        }

        if (sets.Count == 0) return;
        AddSection(T("Связки экипировки", "Equipment combinations"));
        foreach (var (title, names) in sets)
        {
            DetailsPanel.Children.Add(Meta(title));
            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var related = FindEquipment(name);
                if (related?.Id == item.Id) continue;
                if (related is not null) AddLink(related.Name, () => OpenItem(related.Id));
                else DetailsPanel.Children.Add(Meta(name));
            }
        }

        DatabaseItem? FindEquipment(string name) =>
            _database.Items.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? _database.Items.FirstOrDefault(candidate => string.Equals(
                Regex.Replace(candidate.Name, @"\s+\[\d+\]$", ""), name, StringComparison.OrdinalIgnoreCase));
    }
}
