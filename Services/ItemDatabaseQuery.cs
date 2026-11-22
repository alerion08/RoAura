using System.Text.RegularExpressions;
using RoAura.Models;

namespace RoAura.Services;

internal sealed class ItemDatabaseQuery
{
    private readonly (string Text, bool Excluded)[] _terms;

    internal ItemDatabaseQuery(string query)
    {
        if (!query.Contains('"') && !Regex.IsMatch(query, @"(?:^|\s)-\S"))
        {
            _terms = query.Length == 0 ? [] : [(query, false)];
            return;
        }

        _terms = Regex.Matches(query, """(-?)(?:"([^"]+)"|([^\s"]+))""")
            .Select(match => (match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value,
                match.Groups[1].Value == "-")).Where(term => term.Item1.Length > 0).ToArray();
    }

    internal bool Matches(DatabaseItem item, bool includeEffects) => _terms.All(term =>
    {
        bool Contains(string value) => value.Contains(term.Text, StringComparison.OrdinalIgnoreCase);
        var found = Contains(item.Name) || Contains(item.Id.ToString()) || Contains($"#{item.Id}") ||
            (includeEffects && (Contains(item.Type) || Contains(item.Description) || item.Stats.Any(stat => Contains($"{stat.Key} {stat.Value}"))));
        return found != term.Excluded;
    });

    internal static int Slots(DatabaseItem item) =>
        int.TryParse(item.Stats.GetValueOrDefault("slots"), out var count) ? count :
        int.TryParse(Regex.Match(item.Name, @"\[(\d+)\]$").Groups[1].Value, out count) ? count :
        int.TryParse(Regex.Match(item.Description, @"(?im)^\s*slots\s*:\s*(\d+)").Groups[1].Value, out count) ? count : 0;
}
