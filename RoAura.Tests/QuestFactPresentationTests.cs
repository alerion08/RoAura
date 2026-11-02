using RoAura.Models;
using RoAura.Services;
using Xunit;
namespace RoAura.Tests;
public sealed class QuestFactPresentationTests
{
 private static SemanticFact Fact(string kind,string amount,string? branch=null,string? condition=null)=>new(Guid.NewGuid().ToString(),kind,null,amount,condition,branch,null);
 [Fact] public void AlternativeExperienceUsesRangesBeforeAccessAndItems()
 {
  var facts=new[]{Fact("item","1"),Fact("job_exp","300000","low"),Fact("access","1"),Fact("base_exp","2000000","high"),Fact("base_exp","800000","low"),Fact("job_exp","1000000","high")};
  var result=QuestFactPresentation.Rewards(facts);
  Assert.Equal(new[]{"base_exp","job_exp","access","item"},result.Select(f=>f.Kind));
  Assert.Equal("800000–2000000",result[0].Amount);Assert.Equal("300000–1000000",result[1].Amount);
  Assert.Equal(6,facts.Length);
 }
 [Fact] public void SequentialAndUnknownGrantsAreNotCollapsedAsAlternatives()
 {
  var sequential=new[]{Fact("base_exp","1500000"),Fact("base_exp","1000000",condition:"Additional grant")};
  Assert.Equal(2,QuestFactPresentation.Rewards(sequential).Count);
  Assert.Equal(2,QuestFactPresentation.Rewards([Fact("base_exp","unknown","a"),Fact("base_exp","1000000","b")]).Count);
 }
 [Fact] public void RequiredLevelAndQuestsPrecedeLoot()
 {
  var q=new SemanticQuest("q","Quest",60,null,"ready","unknown",new("url","revision","wiki",null),[],[Fact("item","5"),Fact("quest_completed","1"),Fact("base_level","60")],[]);
  Assert.Equal(new[]{"base_level","quest_completed","item"},QuestFactPresentation.Requirements(q).Select(f=>f.Kind));
 }
 [Fact] public void CatalogUsesOnlyReadyIdsAndKeepsNewReadyQuestsReadable()
 {
  var q=new SemanticQuest("new","New",60,null,"ready","unknown",new("url","revision","wiki",null),[],[],[]);
  var ready=new SemanticCatalog(new Dictionary<string,SemanticQuest>{{q.Id,q}},"version");
  Assert.Empty(QuestReadyCatalog.Build(null,null));
  var guide=Assert.Single(QuestReadyCatalog.Build(ready,new QuestCatalog([])));
  Assert.Equal("new",guide.Key);Assert.Equal(60,guide.BaseLevel);
 }
 [Fact] public void RussianCommentTranslationPreservesEnglishOriginal()
 {
  const string en="Disguise equipment; must be worn, not consumed.";
  Assert.Equal(en,QuestFactText.Localize(en,"en"));
  Assert.Equal("Экипировка для маскировки: должна быть надета, не расходуется.",QuestFactText.Localize(en,"ru"));
  Assert.Contains("60–69",QuestFactText.Localize("Base level 60-69 only.\nBase level 90-99 only.","ru"));
 }
}
