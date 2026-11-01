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
 [Fact] public void ExplicitCoordinatesIncludeEveryLocalizedPointWithoutDuplicates()
 {
  var text="(lhz_dun01 147 106) | (lhz_dun01 239 135) | (lhz_dun01 66 213) | (lhz_dun01 225 198) | (lhz_dun02 244 229) | (lhz_dun02 60 298) | (lhz_dun02 267 278) | (lhz_dun02 94 199) | (lhz_dun03 244 51) | (lhz_dun03 123 191) | (lhz_dun03 74 140)";
  var step=Step(2) with {FullRu=text,FullEn=text,Points=[new("db","lhz_dun02",94,199,"target",null,null)]};
  var points=SemanticCoordinates.All(step);
  Assert.Equal(11,points.Count);Assert.Equal(11,points.Select(p=>(p.Map,p.X,p.Y)).Distinct().Count());
  Assert.Contains(points,p=>p.Map=="lhz_dun01" && p.X==147 && p.Y==106);
 }
 [Fact] public void CoordinateHighlightIncludesNumericCellsAndPreservesSurroundingText()
 {
  var text="Visit lhz_dun01 147 106 and return.";
  var length=SemanticCoordinates.HighlightLength(text,6,9);
  Assert.Equal("lhz_dun01 147 106",text.Substring(6,length));
  Assert.Equal(" and return.",text[(6+length)..]);
 }
 [Fact] public void LiteralCoordinatesDoNotGuessUnknownMapsOrCells()
 {
  var step=Step(1) with {FullRu="niflheim 313 70; level 100 200; lhz_dun01 999 11",Mentions=[new("full_ru",0,8,"map","m","niflheim")]};
  var point=Assert.Single(SemanticCoordinates.All(step));Assert.Equal("niflheim",point.Map);Assert.Equal(313,point.X);Assert.Equal(70,point.Y);
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
