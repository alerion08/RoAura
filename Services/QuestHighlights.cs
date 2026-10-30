using System.Text.RegularExpressions;
namespace RoAura.Services;

public sealed record QuestHighlight(string Text, string Kind);
public static class QuestHighlights
{
    public static IReadOnlyList<QuestHighlight> Split(string text,IEnumerable<string> answers,IEnumerable<string> items)
    {
        if(text.Length==0)return [];
        var literals=answers.Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(s=>s,_=>"answer",StringComparer.OrdinalIgnoreCase);
        foreach(var item in items.Where(s=>s.Length>1 && text.Contains(s,StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))literals.TryAdd(item,"item");
        if(literals.Count==0)return [new(text,"")];
        var pattern=@"(?<![\p{L}\p{N}_])(?:"+string.Join("|",literals.Keys.OrderByDescending(s=>s.Length).Select(Regex.Escape))+@")(?![\p{L}\p{N}_])";
        var result=new List<QuestHighlight>();var offset=0;
        foreach(Match match in Regex.Matches(text,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
        {
            if(match.Index>offset)result.Add(new(text[offset..match.Index],""));
            result.Add(new(match.Value,literals[match.Value]));offset=match.Index+match.Length;
        }
        if(offset<text.Length)result.Add(new(text[offset..],""));
        return result;
    }
}
