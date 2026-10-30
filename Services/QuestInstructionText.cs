using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Text.RegularExpressions;
using RoAura.Models;
namespace RoAura.Services;
internal static class QuestInstructionText
{
    private static readonly Lazy<string[]> Items=new(()=>ItemCatalogService.Load(GameDatabaseVersion.SpringRo).Values
        .Select(i=>Regex.Replace(i.Name,@"\s*\[\d+\]\s*$","")).Where(n=>n.Length>1).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    internal static void Apply(TextBlock block,string text,bool darkBackground=false)
    {
        block.Text="";
        foreach(var part in QuestHighlights.Split(text,QuestAnswers.Extract(text),Items.Value))
        {
            var run=new Run(part.Text);
            if(part.Kind.Length>0)
            {
                run.FontWeight=FontWeights.Bold;
                run.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(part.Kind=="answer"?(darkBackground?"#D79CFF":"#80249B"):(darkBackground?"#75E28C":"#217B37")));
            }
            block.Inlines.Add(run);
        }
    }
}
