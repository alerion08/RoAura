using System.Text.RegularExpressions;
using RoAura.Models;

namespace RoAura.Services;

public sealed record QuestNavigationTarget(string Map, int X, int Y)
{
    public string Command => $"/navi {Map} {X}/{Y}";
    public override string ToString() => $"{Map} {X},{Y}";
}
public static class QuestNavigation
{
    public static IReadOnlyList<QuestNavigationTarget> Targets(QuestListRow row) => row.Progress.NeedsStepReview || row.Progress.IsMissingFromCatalog
        ? [] : row.Guide?.Steps.SingleOrDefault(s => s.Number == row.Progress.CurrentStep)?.Locations
            .Where(l => l.Kind == "map_coord" && l.Assurance == "wiki_literal_not_npc_verified" && l.Map is not null &&
                Regex.IsMatch(l.Map, @"\A[a-zA-Z0-9_@.-]{1,32}\z") && l.X is >= 0 and <= 1023 && l.Y is >= 0 and <= 1023)
            .Select(l => new QuestNavigationTarget(l.Map!, l.X, l.Y)).Distinct().ToList() ?? [];
}
