using System.Globalization;
using RoAura.Models;
namespace RoAura.Services;

public static class QuestFactPresentation
{
 public static bool IsTopRequirement(SemanticFact fact)=>fact.StepId is null || fact.Kind is "base_level" or "job_level" or "quest_completed" or "conditional_quest_steps";
 public static IReadOnlyList<SemanticFact> Requirements(SemanticQuest quest)
 {
  var facts=quest.Requirements.Where(IsTopRequirement).Select(f=>f.Kind=="quest_completed" && f.Name is null && !string.IsNullOrWhiteSpace(f.Amount) && !long.TryParse(f.Amount,out _)?f with{Name=f.Amount,Amount=null}:f).ToList();
  if(quest.BaseLevel is {} level && !facts.Any(f=>f.Kind=="base_level"))facts.Add(new("header:base_level","base_level",null,level.ToString(CultureInfo.InvariantCulture),null,null,null));
  if(quest.JobLevel is {} job && !facts.Any(f=>f.Kind=="job_level"))facts.Add(new("header:job_level","job_level",null,job.ToString(CultureInfo.InvariantCulture),null,null,null));
  return facts.OrderBy(f=>f.Kind switch{"base_level"=>0,"job_level"=>1,"quest_completed" or "conditional_quest_steps"=>2,_=>3}).ToList();
 }
 public static IReadOnlyList<SemanticFact> Rewards(IReadOnlyList<SemanticFact> source)
 {
  var result=new List<SemanticFact>();
  foreach(var kind in new[]{"base_exp","job_exp"})
  {
   var group=source.Where(f=>f.Kind==kind || f.Kind==kind+"_total").ToList();
   // Range across alternative grants, never an arithmetic sum. Unspecified or
   // approximate amounts remain separate to preserve their source qualification.
   if(group.Count>1 && group.All(f=>f.Branch is not null && long.TryParse(f.Amount,NumberStyles.None,CultureInfo.InvariantCulture,out _)))
   {
    var amounts=group.Select(f=>long.Parse(f.Amount!,CultureInfo.InvariantCulture)).ToList();
    var min=amounts.Min();var max=amounts.Max();
    result.Add(new("range:"+kind,kind,null,min==max?min.ToString(CultureInfo.InvariantCulture):$"{min}–{max}",
      "Reward varies by route or character level.\n"+string.Join("\n",group.Select(f=>f.Condition).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct()),null,null));
   }
   else result.AddRange(group);
  }
  result.AddRange(source.Where(f=>f.Kind is not ("base_exp" or "base_exp_total" or "job_exp" or "job_exp_total"))
   .OrderBy(f=>f.Kind is "access" or "unlock" or "service_unlock"?0:1));
  return result;
 }
}
