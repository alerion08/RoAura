using RoAura.Models;
namespace RoAura.Services;

public static class QuestReadyCatalog
{
 public static IReadOnlyList<QuestGuide> Build(SemanticCatalog? ready,QuestCatalog? legacy)
 {
  if(ready is null)return [];
  return ready.Quests.Values.Select(q=>legacy?.Guides.GetValueOrDefault(q.Id)??new QuestGuide(
   q.Id,q.Title,q.Source.Url,"unknown",q.Review,q.BaseLevel,q.JobLevel,null,null,null,"unknown",null,null,"other",null,
   q.Steps.Select(s=>new QuestStep(s.Order,s.SourceNumber,s.Kind,null,s.Original,[])).ToList()))
   .OrderBy(g=>g.Title,StringComparer.OrdinalIgnoreCase).ToList();
 }
}
