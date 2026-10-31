using System.Text.RegularExpressions;
using RoAura.Models;
namespace RoAura.Services;

// Matches research enrich.py at 48c9dd2: literal wiki map + cells only.
public static class QuestWikiLocations
{
    private static readonly Regex Coordinates=new(@"\b([a-z][a-z0-9_]{2,24})\s*[, ]?\s*\(?\s*(\d{1,3})\s*(?:,\s*|\s+)(\d{1,3})(?!\d)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    private static readonly Regex Context=new(@"\b(?:at|in|to|near|inside|around|on|visit|enter)\s+(?:the\s+)?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    public static IReadOnlyList<QuestLocation> Parse(string text,IReadOnlySet<string> knownMaps)
    {
        var result=new List<QuestLocation>();
        foreach(Match match in Coordinates.Matches(text))
        {
            var map=match.Groups[1].Value.ToLowerInvariant();
            if(!int.TryParse(match.Groups[2].Value,out var x) || !int.TryParse(match.Groups[3].Value,out var y))continue;
            if(x>400 || y>400)continue;
            var pair=text[match.Groups[2].Index..(match.Groups[3].Index+match.Groups[3].Length)];
            var prefix=text[Math.Max(0,match.Index-40)..match.Index];
            if(!knownMaps.Contains(map) && !(map.Contains('_') && (pair.Contains(',') || Context.IsMatch(prefix))))continue;
            result.Add(new(map,x,y,"map_coord",match.Value,"wiki_literal_not_npc_verified"));
        }
        return result.DistinctBy(l=>(l.Map,l.X,l.Y)).ToArray();
    }
}
