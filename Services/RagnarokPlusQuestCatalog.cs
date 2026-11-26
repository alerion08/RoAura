using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoAura.Models;

namespace RoAura.Services;

internal sealed class RagnarokPlusQuestCatalog
{
    internal sealed record Material(int Id, string Name, int? Quantity, string Role, string? Condition);
    internal sealed record Monster(int? Id, string Name, string? Count);
    internal sealed record Point(string Map, int X, int Y, string Name);
    internal sealed record Reputation(string Faction, int Value, string Multiplier, string Mode);
    internal sealed record MapImage(string Url, string Code, QuestMapPin[] Pins);
    internal sealed record Step(int Number, string Title, string Text, string? Npc, Point[] Points,
        Material[] Items, Monster[] Monsters, string? Image, MapImage[]? MapImages);
    internal sealed record Card(string Path, string Kind, string Title, int? Level, Step[] Steps,
        Reputation[] Reputation, Material[] Requirements)
    {
        public string Key => "ragnarokplus:" + Path;
        public bool Hunting => Kind == "hunting";
        public bool AllClan => Hunting && Title.StartsWith("All Clan:", StringComparison.OrdinalIgnoreCase);
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

    private static bool HasRewardSummary(Card card) => !card.Hunting && card.Steps.Length > 1 &&
        card.Steps[^1] is { Title: "Quest Complete", Points.Length: 0, Monsters.Length: 0 } step &&
        step.Text.StartsWith("Quest Complete\n", StringComparison.Ordinal) &&
        !step.Items.Any(item => item.Role == "bring");

    private static Monster[] HuntTargets(Step step)
    {
        var start = step.Text.IndexOf("\nTARGETS\n", StringComparison.Ordinal);
        if (start < 0) return step.Monsters;
        var section = step.Text[(start + "\nTARGETS\n".Length)..].Split("\nREWARD", 2)[0];
        var targets = Regex.Matches(section, @"(?m)^([^\n]+)\nx([0-9][0-9 ,\u00a0\u202f]*)$")
            .Select(match =>
            {
                var name = match.Groups[1].Value.Trim();
                var id = step.Monsters.FirstOrDefault(monster => string.Equals(monster.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
                return new Monster(id, name, "x" + match.Groups[2].Value.Trim());
            }).ToArray();
        return targets.Length == 0 ? step.Monsters : targets;
    }

    private static string HuntInstruction(IEnumerable<Monster> targets, bool russian)
    {
        var lines = targets.Select(target =>
        {
            var count = target.Count?.TrimStart('x', '×');
            return string.Join(" ", new[] { russian ? "Убей" : "Kill", count, target.Name }.Where(value => !string.IsNullOrWhiteSpace(value))) + ".";
        }).ToArray();
        return lines.Length == 0 ? (russian ? "Цели не указаны." : "Targets unavailable.") : string.Join("\n", lines);
    }

    private static IEnumerable<Step> ActionSteps(Card card) => HasRewardSummary(card) ? card.Steps.SkipLast(1) : card.Steps;

    private static QuestStep GuideStep(Card card, Step step, int index, bool original = false) =>
        new(index + 1, step.Number, card.Kind, step.Title,
            card.Hunting && !original ? HuntInstruction(HuntTargets(step), false) : Instruction(step),
            step.Points.Select(point => new QuestLocation(point.Map, point.X, point.Y,
                "explicit", point.Name, "source")).ToArray());

    internal bool MigratePresentationChange(QuestProgressEntry entry, QuestGuide guide)
    {
        if (!Cards.TryGetValue(entry.QuestKey, out var card) || (!card.Hunting && !HasRewardSummary(card))) return false;
        if (card.Hunting)
        {
            var previous = GuideStep(card, card.Steps[0], 0, original: true);
            var current = guide.Steps[0];
            var oldCompletion = QuestProgress.CompletionKey(previous);
            var newCompletion = QuestProgress.CompletionKey(current);
            if (entry.CompletedStepAnchors.Remove(oldCompletion) && !entry.CompletedStepAnchors.Contains(newCompletion))
                entry.CompletedStepAnchors.Add(newCompletion);
            if (entry.StepAnchor != QuestProgress.Anchor(previous)) return false;
        }
        else if (entry.StepAnchor != QuestProgress.Anchor(GuideStep(card, card.Steps[^1], card.Steps.Length - 1)))
            return false;
        QuestProgress.SelectStep(entry, guide, guide.Steps[^1].Number);
        return true;
    }

    private static QuestGuide Guide(Card card, bool duplicate) => new(card.Key, card.Title + (duplicate ? " (R+)" : ""),
        SourceUrl(card), "available", "source", card.Level, null, null, null, null, "source", null, null,
        card.Hunting ? "rplus-hunting" : "rplus-quest", "source",
        ActionSteps(card).Select((step, index) => GuideStep(card, step, index)).ToArray());

    internal SemanticQuest CreateSemantic(Card card, string title)
    {
        var steps = ActionSteps(card).Select((step, index) =>
        {
            var id = card.Key + ":" + (index + 1).ToString(CultureInfo.InvariantCulture);
            var monsters = card.Hunting ? HuntTargets(step) : step.Monsters;
            var text = card.Hunting ? HuntInstruction(monsters, false) : Instruction(step);
            var russian = card.Hunting ? HuntInstruction(monsters, true) : text;
            return new SemanticStep(id, card.Key, index + 1, step.Number, card.Kind, text,
                card.Hunting ? russian : step.Title, card.Hunting ? text : step.Title, russian, text, [],
                step.Points.Select((point, order) => new SemanticPoint(id + ":" + order, point.Map,
                    point.X, point.Y, "target", order, null)).ToArray(),
                step.Items.Select(item => new SemanticItem(item.Id.ToString(CultureInfo.InvariantCulture), item.Name,
                    item.Quantity, item.Role == "bring" ? "bring" : item.Role, item.Role == "bring", item.Condition, null)
                    { DatabaseItemId = item.Id }).ToArray(), [])
            {
                Npcs = step.Npc is null ? [] : [step.Npc],
                Images = step.MapImages is { Length: > 0 } maps
                    ? maps.Select(map => new SemanticImage(map.Url, null, "map", map.Code, "source") { Pins = map.Pins }).ToArray()
                    : step.Image is null ? [] : [new SemanticImage(step.Image, null, "source", null, "source")],
                DatabaseMonsters = monsters
            };
        }).ToArray();
        var requirements = card.Requirements.Where(item => item.Role == "bring")
            .Select(item => new SemanticFact(item.Id.ToString(), "item", item.Name, item.Quantity?.ToString(), item.Condition, null, null) { DatabaseItemId = item.Id })
            .ToArray();
        var rewards = card.Reputation.Select((rep, index) => new SemanticFact(index.ToString(), "reputation", rep.Faction,
            rep.Value + " " + rep.Multiplier, rep.Mode, null, null)).ToList();
        if (HasRewardSummary(card))
        {
            var summary = card.Steps[^1];
            rewards.Add(new SemanticFact("source-reward", "reward", summary.Text["Quest Complete\n".Length..].Trim(),
                null, null, null, null));
            rewards.AddRange(summary.Items.DistinctBy(item => item.Id).Select(item =>
                new SemanticFact("reward-link:" + item.Id, "item", item.Name, null, null, null, null)
                { DatabaseItemId = item.Id }));
        }
        if (card.Hunting)
        {
            var raw = card.Steps[0].Text;
            foreach (Match exp in Regex.Matches(raw, @"(?m)^(Base|Job) EXP\n([0-9][0-9 ,\u00a0\u202f]*)$"))
                rewards.Add(new SemanticFact(exp.Groups[1].Value, exp.Groups[1].Value.ToLowerInvariant() + "_exp",
                    null, exp.Groups[2].Value, null, null, null));
            rewards.AddRange(card.Steps.SelectMany(step => step.Items).Where(item => item.Role == "reward")
                .Select(item => new SemanticFact("reward:" + item.Id, "item", item.Name, item.Quantity?.ToString(), null, null, null)
                { DatabaseItemId = item.Id }));
        }
        return new SemanticQuest(card.Key, title, card.Level, null, "source", "Ragnarok+",
            new(SourceUrl(card), "collector", "Ragnarok+", null), steps, requirements, rewards);
    }
}
