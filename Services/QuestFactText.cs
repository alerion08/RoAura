using System.Text.Json;
namespace RoAura.Services;

/// <summary>Reviewed display translations, keyed by exact source comment.
/// Original semantic records and their evidence remain unchanged.</summary>
public static class QuestFactText
{
 private static readonly Lazy<IReadOnlyDictionary<string,string>> Russian=new(()=>
  JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data","quest_fact_comments.ru.json")))
  ??new Dictionary<string,string>());
 public static string Localize(string? text,string language)
 {
  if(string.IsNullOrEmpty(text))return "";
  if(language!="ru")return text;
  if(Russian.Value.TryGetValue(text,out var translated))return translated;
  // Grouped experience conditions retain each original alternative's note.
  return string.Join("\n",text.Split('\n').Select(line=>Russian.Value.GetValueOrDefault(line,line)));
 }
 public static bool HasRussian(string text)=>Russian.Value.ContainsKey(text);
}
