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
}
