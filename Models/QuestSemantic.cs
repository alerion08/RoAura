using System.Text;
namespace RoAura.Models;

public sealed record SemanticMention(string Field,int Start,int End,string Type,string EntityId,string Surface);
public sealed record SemanticPoint(string Id,string? Map,int X,int Y,string Role,int? Order,string? Condition);
public sealed record SemanticItem(string Id,string Name,int? Quantity,string Operation,bool? Required,string? Condition,string? Branch);
public sealed record SemanticFact(string Id,string Kind,string? Name,string? Amount,string? Condition,string? Branch,string? StepId);
public sealed record SemanticAnswer(string Id,string Text,bool? Correct,string Operation,string? Condition,string? Branch);
public sealed record SemanticImage(string Url,string? LocalPath,string Purpose,string? Caption,string Rights);
public sealed record SemanticSource(string Url,string Revision,string Kind,string? License);
public sealed record SemanticStep(string Id,string QuestId,int Order,int? SourceNumber,string? Kind,string Original,
 string? ShortRu,string? ShortEn,string? FullRu,string? FullEn,
 IReadOnlyList<SemanticMention> Mentions,IReadOnlyList<SemanticPoint> Points,IReadOnlyList<SemanticItem> Items,IReadOnlyList<SemanticAnswer> Answers)
{
 public IReadOnlyList<string> Npcs {get;init;}=[];
 public IReadOnlyList<SemanticImage> Images {get;init;}=[];
 public string Short(string language)=>language=="ru"?ShortRu!:ShortEn!;
 public string Full(string language)=>language=="ru"?FullRu!:FullEn!;
}
public sealed record SemanticQuest(string Id,string Title,int? BaseLevel,int? JobLevel,string Review,string Availability,
 SemanticSource Source,IReadOnlyList<SemanticStep> Steps,IReadOnlyList<SemanticFact> Requirements,IReadOnlyList<SemanticFact> Rewards)
{ public override string ToString()=>Title; }
public sealed record SemanticCatalog(IReadOnlyDictionary<string,SemanticQuest> Quests,string Version);

public static class SemanticReadiness
{
 // Compare distinct identities scoped to the owning quest; never expose just the ready subset.
 public static bool IsReady(string questId,IReadOnlyList<SemanticStep> steps,IReadOnlySet<(string Quest,string Step)> ready)=>
  steps.Count>0 && steps.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()==steps.Count &&
  steps.All(s=>s.QuestId==questId && ready.Contains((questId,s.Id)) &&
   new[]{s.ShortRu,s.ShortEn,s.FullRu,s.FullEn}.All(v=>!string.IsNullOrWhiteSpace(v)));
 public static bool MatchesProgress(SemanticQuest semantic,QuestGuide? guide)=>guide is not null && semantic.Id==guide.Key &&
  semantic.Steps.Count==guide.Steps.Count && semantic.Steps.Zip(guide.Steps).All(p=>p.First.Order==p.Second.Number &&
   p.First.Kind==p.Second.Kind && p.First.Original==p.Second.Instruction);
 // Database offsets are Unicode code points, WPF and C# indices are UTF-16.
 public static (int Start,int Length)? Utf16Span(string text,int start,int end,string surface)
 {
  if(start<0 || end<=start)return null;
  var boundaries=new List<int>{0};var offset=0;
  foreach(var rune in text.EnumerateRunes()){offset+=rune.Utf16SequenceLength;boundaries.Add(offset);}
  if(end>=boundaries.Count)return null;
  var a=boundaries[start];var length=boundaries[end]-a;
  return text.Substring(a,length)==surface?(a,length):null;
 }
}
