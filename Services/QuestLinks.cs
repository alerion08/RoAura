using System.Text.RegularExpressions;
using RoAura.Models;

namespace RoAura.Services;

public sealed record QuestTextPart(string Text, QuestGuide? Guide);
public static class QuestLinks
{
    // Exact literal catalog titles only. Ambiguous names remain text; longest match wins.
    public static IReadOnlyList<QuestTextPart> Split(string text, QuestCatalog? catalog, string? currentKey = null)
    {
        if (catalog is null) return [new(text, null)];
        var titles = catalog.Guides.Values.GroupBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1 && g.First().Key != currentKey && g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (titles.Count == 0) return [new(text, null)];
        var pattern = @"(?<![\p{L}\p{N}_])(?:" + string.Join("|", titles.Keys.OrderByDescending(t => t.Length).Select(Regex.Escape)) + @")(?![\p{L}\p{N}_])";
        var result = new List<QuestTextPart>(); var offset = 0;
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (match.Index > offset) result.Add(new(text[offset..match.Index], null));
            result.Add(new(match.Value, titles[match.Value])); offset = match.Index + match.Length;
        }
        if (offset < text.Length) result.Add(new(text[offset..], null));
        return result;
    }
}
