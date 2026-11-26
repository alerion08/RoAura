using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class RagnarokPlusQuestCatalogTests
{
    [Fact]
    public void SnapshotKeepsRegularAndHuntingQuestsDistinct()
    {
        var source = RagnarokPlusQuestCatalog.Instance;
        Assert.Equal(122, source.Cards.Values.Count(card => !card.Hunting));
        Assert.Equal(178, source.Cards.Values.Count(card => card.Hunting));
        var card = source.Cards["ragnarokplus:/hunting-db/75170"];
        Assert.Equal(7, card.Reputation.Select(rep => rep.Faction).Distinct().Count());
        Assert.Contains(card.Reputation, rep => rep.Faction == "Mage School" && rep.Value == 50 && rep.Multiplier == "x3");
        var step = Assert.Single(card.Steps);
        Assert.Contains(step.Monsters, monster => monster.Id == 30022);
        Assert.Contains(step.Items, item => item.Id == 40009 && item.Role == "reward");
    }

    [Fact]
    public void QuestLootLookupIsServerScopedAndUsesOnlyRequiredItems()
    {
        var source = RagnarokPlusQuestCatalog.Instance;
        Assert.Contains(source.RequiredFor(GameServerProfile.RagnarokPlus, 4001),
            card => card.Path == "/quest-db/koshei-the-immortal");
        Assert.Empty(source.RequiredFor(GameServerProfile.SpringRo, 4001));
        Assert.DoesNotContain(source.RequiredFor(GameServerProfile.RagnarokPlus, 40009),
            card => card.Path == "/hunting-db/75170");
    }

    [Fact]
    public void DuplicateNamesAreQualifiedAndBothReadersShareProgressIdentity()
    {
        var source = RagnarokPlusQuestCatalog.Instance;
        var legacy = new QuestGuide("classic:id", "Koshei the Immortal Quest", "", "", "",
            null, null, null, null, null, "", null, null, null, null, []);
        var guides = source.Combine(new QuestCatalog([legacy]));
        var card = source.Cards["ragnarokplus:/quest-db/koshei-the-immortal"];
        var guide = guides.Guides[card.Key];
        Assert.Equal("Koshei the Immortal (R+)", guide.Title);
        Assert.True(SemanticReadiness.MatchesProgress(source.CreateSemantic(card, guide.Title), guide));
    }
    [Fact]
    public void EveryHuntingDescriptionContainsOnlyKillObjectives()
    {
        var source = RagnarokPlusQuestCatalog.Instance;
        foreach (var card in source.Cards.Values.Where(card => card.Hunting))
        {
            var step = Assert.Single(source.CreateSemantic(card, card.Title).Steps);
            Assert.NotEmpty(step.DatabaseMonsters);
            Assert.All(step.FullEn!.Split('\n'), line => Assert.StartsWith("Kill ", line));
            Assert.All(step.FullRu!.Split('\n'), line => Assert.StartsWith("Убей ", line));
        }
        var unlinked = Assert.Single(source.Cards.Values, card => card.Title == "Archer Clan: Tick Tock");
        var targets = Assert.Single(source.CreateSemantic(unlinked, unlinked.Title).Steps).DatabaseMonsters;
        Assert.Contains(targets, monster => monster.Name == "Penomena" && monster.Count == "x255" && monster.Id is null);
        var allClan = source.Cards["ragnarokplus:/hunting-db/75170"];
        Assert.True(QuestsPage.MatchesHunting(allClan, "All Clan", 50));
        Assert.False(QuestsPage.MatchesHunting(unlinked, "All Clan", null));
    }

    [Fact]
    public void RewardSummaryLeavesActionStepsAndMigratesOnlyItsKnownAnchor()
    {
        var source = RagnarokPlusQuestCatalog.Instance;
        var card = Assert.Single(source.Cards.Values, entry => entry.Title == "Sight Blaster");
        var guide = source.Combine(new QuestCatalog([])).Guides[card.Key];
        var semantic = source.CreateSemantic(card, guide.Title);
        Assert.Equal(4, guide.Steps.Count);
        Assert.True(SemanticReadiness.MatchesProgress(semantic, guide));
        Assert.Contains(semantic.Rewards, reward => reward.Name == "Learn Sight Blaster");

        var removed = card.Steps[^1];
        var previous = new QuestStep(5, removed.Number, card.Kind, removed.Title, removed.Title + "\n" + removed.Text, []);
        var progress = new QuestProgressEntry { QuestKey = card.Key, CurrentStep = 5, StepAnchor = QuestProgress.Anchor(previous) };
        Assert.True(source.MigratePresentationChange(progress, guide));
        Assert.Equal(4, progress.CurrentStep);
        Assert.False(progress.NeedsStepReview);
    }
}
