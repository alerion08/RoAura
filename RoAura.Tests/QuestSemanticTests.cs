using System.Security.Cryptography;
using RoAura.Models;
using RoAura.Services;
using Xunit;
namespace RoAura.Tests;
public sealed class QuestSemanticTests
{
 private static SemanticStep Step(int order,string? ru="RU")=>new("s"+order,"q",order,order,"numbered","original",ru,"EN","полное","full",[],[],[],[]);
 [Theory]
 [InlineData(0,0,false)] [InlineData(1,1,true)] [InlineData(10,10,true)] [InlineData(10,9,false)]
 public void WholeQuestAdmission(int count,int readyCount,bool expected)
 {
  var steps=Enumerable.Range(1,count).Select(i=>Step(i)).ToList();
  var ready=Enumerable.Range(1,readyCount).Select(i=>("q","s"+i)).ToHashSet();
  Assert.Equal(expected,SemanticReadiness.IsReady("q",steps,ready));
 }
 [Theory] [InlineData(null)] [InlineData("")] [InlineData(" \t\n")]
 public void EmptyTranslationsRejectWholeQuest(string? value)=>Assert.False(SemanticReadiness.IsReady("q",[Step(1,value)],new HashSet<(string,string)>{("q","s1")}));
 [Fact] public void MembershipIsScopedToQuestAndDistinctIdentity()
 {
  Assert.False(SemanticReadiness.IsReady("q",[Step(1)],new HashSet<(string,string)>{("other","s1")}));
  Assert.False(SemanticReadiness.IsReady("q",[Step(1),Step(1)],new HashSet<(string,string)>{("q","s1")}));
 }
 [Fact] public void UpdatedReadinessAdmitsWithoutWhitelist()
 {
  var steps=Enumerable.Range(1,10).Select(i=>Step(i)).ToList();var ready=Enumerable.Range(1,9).Select(i=>("q","s"+i)).ToHashSet();
  Assert.False(SemanticReadiness.IsReady("q",steps,ready));ready.Add(("q","s10"));Assert.True(SemanticReadiness.IsReady("q",steps,ready));
 }
 [Fact] public void UnicodeMentionsConvertCodePointsAndVerifySurface()
 {
  Assert.Equal((3,3),SemanticReadiness.Utf16Span("😀 NPC!",2,5,"NPC"));
  Assert.Null(SemanticReadiness.Utf16Span("😀 NPC!",2,5,"bad"));
  Assert.Null(SemanticReadiness.Utf16Span("test",1,100,"bad"));
 }
 [Fact] public async Task ActualSemanticSnapshotIsReadOnlyAndPartialReviewDoesNotExcludeLocalizedQuests()
 {
  var path=Environment.GetEnvironmentVariable("ROAURA_QUEST_SEMANTIC_TEST_PATH")??Path.Combine(AppContext.BaseDirectory,"Data","quest_semantic.sqlite");
  Assert.True(File.Exists(path),"Pinned semantic fixture must be installed by CI.");
  var hash=SHA256.HashData(File.ReadAllBytes(path));var result=await new QuestSemanticService(path).LoadAsync();
  Assert.NotEmpty(result.Quests);var ayothaya=result.Quests["irowiki-classic:Ayothaya_Dungeon_Entrance_Quest"];
  Assert.Equal(10,ayothaya.Steps.Count);Assert.Contains("partial",ayothaya.Review);Assert.Equal("unknown",ayothaya.Availability);
  Assert.All(result.Quests.Values.SelectMany(q=>q.Steps),s=>Assert.All(new[]{s.ShortRu,s.ShortEn,s.FullRu,s.FullEn},v=>Assert.False(string.IsNullOrWhiteSpace(v))));
  foreach(var step in result.Quests.Values.SelectMany(q=>q.Steps))
   foreach(var mention in step.Mentions)
   {
    var text=mention.Field switch{"short_ru"=>step.ShortRu!,"short_en"=>step.ShortEn!,"full_ru"=>step.FullRu!,"full_en"=>step.FullEn!,_=>throw new InvalidDataException("Unknown semantic text field.")};
    Assert.NotNull(SemanticReadiness.Utf16Span(text,mention.Start,mention.End,mention.Surface));
   }
  var legacyPath=Environment.GetEnvironmentVariable("ROAURA_QUEST_CATALOG_TEST_PATH");
  if(!string.IsNullOrWhiteSpace(legacyPath))
  {
   var legacy=await new QuestCatalogService(legacyPath).LoadAsync();
   Assert.All(result.Quests.Values,q=>Assert.True(SemanticReadiness.MatchesProgress(q,legacy.Guides.GetValueOrDefault(q.Id)),q.Id));
  }
  Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(path)));
 }
}
