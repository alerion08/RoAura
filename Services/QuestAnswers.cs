using System.Text.RegularExpressions;
namespace RoAura.Services;

public static class QuestAnswers
{
    // Literal source instructions only: quoted input, or short capitalized names after "type in".
    // Variable passwords, dialogue/menu selections and research candidates never supply an answer.
    private static readonly Regex Quoted = new("\\b(?:type|input|answer with|enter the (?:text|answer))\\s+(?:(?:in|the following(?: text| answer)?|the text|the answer)\\s*:?\\s*)?(?:[:=]\\s*)?(?:\"(?<text>[^\"\\r\\n]{1,200})\"|“(?<text>[^”\\r\\n]{1,200})”|'(?<text>[^'\\r\\n]{1,200})')", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Named = new(@"\b[Tt]ype in (?<text>[A-Z][A-Za-z0-9'-]*(?: (?:[A-Z][A-Za-z0-9'-]*|of|the)){0,7})(?=\s*(?:[.,|]|and you'll|at the|$))", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    public static IReadOnlyList<string> Extract(string? instruction)
    {
        if(string.IsNullOrEmpty(instruction))return [];
        return Quoted.Matches(instruction).Concat(Named.Matches(instruction)).OrderBy(m=>m.Index)
            .Select(m=>m.Groups["text"].Value).Distinct(StringComparer.Ordinal).ToArray();
    }
}
