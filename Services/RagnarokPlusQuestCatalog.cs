using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

internal sealed class RagnarokPlusQuestCatalog
{
    internal sealed record Material(int Id, string Name, int? Quantity, string Role, string? Condition);
    internal sealed record Monster(int Id, string Name, string? Count);
    internal sealed record Point(string Map, int X, int Y, string Name);
    internal sealed record Reputation(string Faction, int Value, string Multiplier, string Mode);
    internal sealed record Step(int Number, string Title, string Text, string? Npc, Point[] Points,
        Material[] Items, Monster[] Monsters, string? Image);
    internal sealed record Card(string Path, string Kind, string Title, int? Level, Step[] Steps,
        Reputation[] Reputation, Material[] Requirements)
    {
        public string Key => "ragnarokplus:" + Path;
        public bool Hunting => Kind == "hunting";
    }

    private static readonly Lazy<RagnarokPlusQuestCatalog> Value = new(Load);
    internal static RagnarokPlusQuestCatalog Instance => Value.Value;
    internal IReadOnlyDictionary<string, Card> Cards { get; }
    private readonly ILookup<int, string> _requiredBy;

    private RagnarokPlusQuestCatalog(Card[] cards)
    {
        Cards = cards.ToDictionary(card => card.Key);
        _requiredBy = cards.SelectMany(card => card.Steps.SelectMany(step => step.Items).Concat(card.Requirements)
            .Where(item => item.Role == "bring").Select(item => (item.Id, card.Key)).Distinct())
            .ToLookup(row => row.Id, row => row.Key);
    }

    private static RagnarokPlusQuestCatalog Load()
    {
        using var resource = typeof(RagnarokPlusQuestCatalog).Assembly.GetManifestResourceStream("RoAura.RagnarokPlusQuests")
            ?? throw new InvalidDataException("Ragnarok+ quest catalog is missing.");
        using var stream = new GZipStream(resource, CompressionMode.Decompress);
        return new(JsonSerializer.Deserialize<Card[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []);
    }

    internal IEnumerable<Card> RequiredFor(string server, int itemId) =>
        GameServerProfile.Normalize(server) == GameServerProfile.RagnarokPlus
            ? _requiredBy[itemId].Select(key => Cards[key])
            : Enumerable.Empty<Card>();

    private static string NormalizeTitle(string title)
    {
        title = title.Trim();
        if (title.EndsWith(" Quest", StringComparison.OrdinalIgnoreCase)) title = title[..^6];
        return string.Concat(title.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }

    internal QuestCatalog Combine(QuestCatalog legacy)
    {
        var titles = legacy.Guides.Values.Select(guide => NormalizeTitle(guide.Title)).ToHashSet();
        var duplicateNames = Cards.Values.GroupBy(card => NormalizeTitle(card.Title))
            .Where(group => group.Count() > 1).Select(group => group.Key);
        titles.UnionWith(duplicateNames);
        return new QuestCatalog(legacy.Guides.Values.Concat(Cards.Values.Select(card =>
            Guide(card, titles.Contains(NormalizeTitle(card.Title))))));
    }

    private static string SourceUrl(Card card) => "https://ragnarok.plus" + card.Path;
    private static string Instruction(Step step) => step.Title + "\n" + step.Text;

    private static QuestGuide Guide(Card card, bool duplicate) => new(card.Key, card.Title + (duplicate ? " (R+)" : ""),
        SourceUrl(card), "available", "source", card.Level, null, null, null, null, "source", null, null,
        card.Hunting ? "rplus-hunting" : "rplus-quest", "source",
        card.Steps.Select((step, index) => new QuestStep(index + 1, step.Number, card.Kind, step.Title,
            Instruction(step), step.Points.Select(point => new QuestLocation(point.Map, point.X, point.Y,
                "explicit", point.Name, "source")).ToArray())).ToArray());

    internal SemanticQuest CreateSemantic(Card card, string title)
    {
        var steps = card.Steps.Select((step, index) =>
        {
            var id = card.Key + ":" + (index + 1).ToString(CultureInfo.InvariantCulture);
            var text = Instruction(step);
            return new SemanticStep(id, card.Key, index + 1, step.Number, card.Kind, text,
                step.Title, step.Title, text, text, [],
                step.Points.Select((point, order) => new SemanticPoint(id + ":" + order, point.Map,
                    point.X, point.Y, "target", order, null)).ToArray(),
                step.Items.Select(item => new SemanticItem(item.Id.ToString(CultureInfo.InvariantCulture), item.Name,
                    item.Quantity, item.Role == "bring" ? "bring" : item.Role, item.Role == "bring", item.Condition, null)
                    { DatabaseItemId = item.Id }).ToArray(), [])
            {
                Npcs = step.Npc is null ? [] : [step.Npc],
                Images = step.Image is null ? [] : [new SemanticImage(step.Image, null, "source", null, "source")],
                DatabaseMonsters = step.Monsters
            };
        }).ToArray();
        var requirements = card.Requirements.Where(item => item.Role == "bring")
            .Select(item => new SemanticFact(item.Id.ToString(), "item", item.Name, item.Quantity?.ToString(), null, null, null))
            .ToArray();
        var rewards = card.Reputation.Select((rep, index) => new SemanticFact(index.ToString(), "reputation", rep.Faction,
            rep.Value + " " + rep.Multiplier, rep.Mode, null, null)).ToArray();
        return new SemanticQuest(card.Key, title, card.Level, null, "source", "Ragnarok+",
            new(SourceUrl(card), "collector", "Ragnarok+", null), steps, requirements, rewards);
    }
}
