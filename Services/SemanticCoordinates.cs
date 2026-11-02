using System.Text.RegularExpressions;
using RoAura.Models;
namespace RoAura.Services;

/// <summary>Keep explicit coordinates from localized prose alongside typed locations.
/// Maps without numeric cells are never assigned guessed coordinates.</summary>
public static class SemanticCoordinates
{
 private static readonly Regex Literal=new(@"\b(?<map>[a-z][a-z0-9_]{2,24})\s*\(?\s*(?<x>\d{1,3})\s*(?:,\s*|/\s*|\s+)(?<y>\d{1,3})(?!\d)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 public static IReadOnlyList<SemanticPoint> All(SemanticStep step)
 {
  var result=step.Points.ToList();
  var maps=step.Mentions.Where(m=>m.Type=="map").Select(m=>m.Surface).Concat(step.Points.Select(p=>p.Map??"")).ToHashSet(StringComparer.OrdinalIgnoreCase);
  foreach(var text in new[]{step.FullRu,step.FullEn,step.ShortRu,step.ShortEn})
   foreach(Match match in Literal.Matches(text??""))
   {
    var map=match.Groups["map"].Value.ToLowerInvariant();
    if(!maps.Contains(map) && !map.Contains('_'))continue;
    var x=int.Parse(match.Groups["x"].Value);var y=int.Parse(match.Groups["y"].Value);
    if(x>400 || y>400)continue;
    if(result.Any(p=>string.Equals(p.Map,map,StringComparison.OrdinalIgnoreCase) && p.X==x && p.Y==y))continue;
    result.Add(new($"literal:{map}:{x}:{y}",map,x,y,"explicit_text",null,null));
   }
  return result;
 }
 public static int HighlightLength(string text,int start,int length)
 {
  var match=Literal.Match(text,start);
  return match.Success && match.Index==start?Math.Max(length,match.Length):length;
 }
}
