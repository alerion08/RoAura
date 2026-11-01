using System.Security.Cryptography;
using RoAura.Models;
namespace RoAura.Services;

/// <summary>Read-only semantic schema 5. No legacy descriptions, parser, network or catalog writes.</summary>
public sealed class QuestSemanticService
{
 public string Path {get;}
 public QuestSemanticService(string? path=null)=>Path=path??System.IO.Path.Combine(AppContext.BaseDirectory,"Data","quest_semantic.sqlite");
 public Task<SemanticCatalog> LoadAsync(CancellationToken token=default)=>Task.Run(()=>Load(token),token);
 private SemanticCatalog Load(CancellationToken token)
 {
  if(!File.Exists(Path))throw new FileNotFoundException("quest_semantic.sqlite is not installed.",Path);
  using var db=new QuestCatalogService.Reader(Path,token);
  db.Query("BEGIN",r=>0);
  if(db.Query("PRAGMA user_version",r=>r.Number(0)).Single()!=5)throw new InvalidDataException("Expected semantic schema 5.");
  if(db.Query("PRAGMA quick_check",r=>r.Text(0)).Single()!="ok" || db.Query("PRAGMA foreign_key_check",r=>0).Count!=0)
   throw new InvalidDataException("Semantic database integrity check failed.");
  var ready=db.Query("SELECT DISTINCT quest_id,step_id FROM ready_steps",r=>(r.Text(0),r.Text(1))).ToHashSet();
  var mentions=db.Query("SELECT step_id,field,start_offset,end_offset,entity_type,entity_id,surface FROM description_mentions",r=>
   (Step:r.Text(0),Value:new SemanticMention(r.Text(1),r.Number(2),r.Number(3),r.Text(4),r.Text(5),r.Text(6)))).GroupBy(x=>x.Step).ToDictionary(g=>g.Key,g=>(IReadOnlyList<SemanticMention>)g.Select(x=>x.Value).ToList());
  var points=db.Query("""
   SELECT sl.step_id,l.location_id,m.map_code,l.x,l.y,sl.role,sl.location_order,sl.condition_text
   FROM step_locations sl JOIN locations l USING(location_id) LEFT JOIN maps m ON m.entity_id=l.map_id
   ORDER BY sl.step_id,sl.location_order IS NULL,sl.location_order,l.location_id
   """,r=>(Step:r.Text(0),Value:new SemanticPoint(r.Text(1),r.NullableText(2),r.Number(3),r.Number(4),r.Text(5),r.NullableNumber(6),r.NullableText(7))))
   .GroupBy(x=>x.Step).ToDictionary(g=>g.Key,g=>(IReadOnlyList<SemanticPoint>)g.Select(x=>x.Value).ToList());
  var items=db.Query("""
   SELECT si.step_id,si.item_id,e.original_name,si.quantity,si.operation,si.required,si.condition_text,si.branch_id
   FROM step_items si JOIN entities e ON e.entity_id=si.item_id ORDER BY si.fact_id
   """,r=>(Step:r.Text(0),Value:new SemanticItem(r.Text(1),r.Text(2),r.NullableNumber(3),r.Text(4),r.NullableNumber(5) is {} v?v==1:null,r.NullableText(6),r.NullableText(7))))
   .GroupBy(x=>x.Step).ToDictionary(g=>g.Key,g=>(IReadOnlyList<SemanticItem>)g.Select(x=>x.Value).ToList());
  var answers=db.Query("""
   SELECT sd.step_id,o.option_id,o.exact_text,o.correct,o.operation,o.condition_text,d.branch_id
   FROM step_dialogues sd JOIN dialogues d USING(dialogue_id) JOIN dialogue_options o USING(dialogue_id)
   ORDER BY sd.step_id,sd.dialogue_order,o.option_order
   """,r=>(Step:r.Text(0),Value:new SemanticAnswer(r.Text(1),r.Text(2),r.NullableNumber(3) is {} v?v==1:null,r.Text(4),r.NullableText(5),r.NullableText(6))))
   .GroupBy(x=>x.Step).ToDictionary(g=>g.Key,g=>(IReadOnlyList<SemanticAnswer>)g.Select(x=>x.Value).ToList());
  var npcs=db.Query("SELECT sn.step_id,e.original_name FROM step_npcs sn JOIN entities e ON e.entity_id=sn.npc_id ORDER BY e.original_name",r=>(Step:r.Text(0),Name:r.Text(1)))
   .GroupBy(x=>x.Step).ToDictionary(g=>g.Key,g=>(IReadOnlyList<string>)g.Select(x=>x.Name).Distinct().ToList());
  var images=db.Query("SELECT step_id,source_url,local_path,purpose,caption,rights_status FROM step_images ORDER BY image_id",r=>(Step:r.Text(0),Image:new SemanticImage(r.Text(1),r.NullableText(2),r.Text(3),r.NullableText(4),r.Text(5))))
   .GroupBy(x=>x.Step).ToDictionary(g=>g.Key,g=>(IReadOnlyList<SemanticImage>)g.Select(x=>x.Image).ToList());
  var steps=db.Query("""
   SELECT s.step_id,s.quest_id,s.step_order,s.source_step,s.source_kind,s.original_instruction,
          d.short_ru,d.short_en,d.full_ru,d.full_en
   FROM quest_steps s LEFT JOIN step_descriptions d USING(step_id) ORDER BY s.quest_id,s.step_order
   """,r=>new SemanticStep(r.Text(0),r.Text(1),r.Number(2),r.NullableNumber(3),r.NullableText(4),r.Text(5),r.NullableText(6),r.NullableText(7),r.NullableText(8),r.NullableText(9),
    mentions.GetValueOrDefault(r.Text(0))??[],points.GetValueOrDefault(r.Text(0))??[],items.GetValueOrDefault(r.Text(0))??[],answers.GetValueOrDefault(r.Text(0))??[]){Npcs=npcs.GetValueOrDefault(r.Text(0))??[],Images=images.GetValueOrDefault(r.Text(0))??[]})
   .GroupBy(s=>s.QuestId).ToDictionary(g=>g.Key,g=>(IReadOnlyList<SemanticStep>)g.ToList());
  var req=db.Query("""
   SELECT r.quest_id,r.requirement_id,r.kind,e.original_name,r.amount,r.condition_text,r.branch_id,r.step_id
   FROM requirements r LEFT JOIN entities e ON e.entity_id=r.entity_id ORDER BY r.requirement_id
   """,r=>(Quest:r.Text(0),Value:new SemanticFact(r.Text(1),r.Text(2),r.NullableText(3),r.NullableText(4),r.NullableText(5),r.NullableText(6),r.NullableText(7))))
   .GroupBy(x=>x.Quest).ToDictionary(g=>g.Key,g=>(IReadOnlyList<SemanticFact>)g.Select(x=>x.Value).ToList());
  var rewards=db.Query("""
   SELECT r.quest_id,r.reward_id,r.kind,e.original_name,r.amount,r.condition_text,r.branch_id
   FROM rewards r LEFT JOIN entities e ON e.entity_id=r.item_id ORDER BY r.reward_id
   """,r=>(Quest:r.Text(0),Value:new SemanticFact(r.Text(1),r.Text(2),r.NullableText(3),r.NullableText(4),r.NullableText(5),r.NullableText(6),null)))
   .GroupBy(x=>x.Quest).ToDictionary(g=>g.Key,g=>(IReadOnlyList<SemanticFact>)g.Select(x=>x.Value).ToList());
  var quests=db.Query("""
   SELECT q.quest_id,q.original_name,q.base_level,q.job_level,q.review_state,q.availability,s.url,s.revision,s.kind,s.license
   FROM quests q JOIN sources s USING(source_id) ORDER BY q.quest_id
   """,r=>new SemanticQuest(r.Text(0),r.Text(1),r.NullableNumber(2),r.NullableNumber(3),r.Text(4),r.Text(5),
    new(r.Text(6),r.Text(7),r.Text(8),r.NullableText(9)),steps.GetValueOrDefault(r.Text(0))??[],req.GetValueOrDefault(r.Text(0))??[],rewards.GetValueOrDefault(r.Text(0))??[]));
  var version=db.Query("SELECT value FROM metadata WHERE key='data_version'",r=>r.Text(0)).FirstOrDefault()??"";
  return new(quests.Where(q=>SemanticReadiness.IsReady(q.Id,q.Steps,ready)).ToDictionary(q=>q.Id,StringComparer.Ordinal),version);
 }
}
