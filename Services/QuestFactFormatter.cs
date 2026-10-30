using System.Text.RegularExpressions;

namespace RoAura.Services;

public sealed record QuestFact(string Label, string Value);
public static class QuestFactFormatter
{
    // Presentation only: recognize literal headings, never infer inventory, AND/OR logic or rewards.
    private static readonly Regex Labels = new(@"(?<!\w)(Base Level|Job Level|Job Class|Class|Item\(s\) \(Consumed\)|Item\(s\) \(Not Consumed\)|Item\(s\)|Quest Prerequisite\(s\)|Quest Corequisite\(s\)|Base Experience|Job Experience|Quest Reward\(s\)|Zeny|Precondition|Other)\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static IReadOnlyList<QuestFact> Parse(string? original)
    {
        if (string.IsNullOrWhiteSpace(original)) return [];
        var matches = Labels.Matches(original);
        if (matches.Count == 0) return [new("", original)];
        var result = new List<QuestFact>();
        if (matches[0].Index > 0) result.Add(new("", original[..matches[0].Index].Trim()));
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : original.Length;
            result.Add(new(matches[i].Groups[1].Value, original[start..end].Trim()));
        }
        return result;
    }
    public static string? LabelKey(string label)=>label.ToLowerInvariant() switch
    {
        "base level"=>"QFactBase", "job level"=>"QFactJob", "job class" or "class"=>"QFactClass",
        "item(s) (consumed)"=>"QFactConsumed", "item(s) (not consumed)"=>"QFactRetained", "item(s)"=>"QFactItems",
        "quest prerequisite(s)"=>"QFactPrerequisites", "quest corequisite(s)"=>"QFactCorequisites",
        "base experience"=>"QFactBaseExp", "job experience"=>"QFactJobExp", "quest reward(s)"=>"QFactOtherRewards",
        "zeny"=>"QFactZeny", "precondition"=>"QFactPrecondition", "other"=>"QFactOther", _=>null
    };
    public static IEnumerable<string> Lines(QuestFact fact) => fact.Label.StartsWith("Item(s)", StringComparison.OrdinalIgnoreCase)
        ? fact.Value.Split(" , ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        : Regex.Split(fact.Value, @"\s+(?=Level \d+[-–])");
}
