using System.Collections.ObjectModel;

namespace RoAura.Models;

// Source language and evidence labels are retained verbatim. None is a live game state.
public sealed record QuestLocation(string? Map, int X, int Y, string Kind, string RawHint, string Assurance);
public sealed record QuestStep(int Number, int? SourceNumber, string? Kind, string? Section,
    string Instruction, IReadOnlyList<QuestLocation> Locations);
public sealed record QuestGuide(string Key, string Title, string SourceUrl, string Status,
    string ReviewState, int? BaseLevel, int? JobLevel, string? Requirements, string? Rewards,
    string? ContentHash, string RefreshState, string? LastSuccessUtc, string? LastError,
    string? Category, string? CategoryAssurance, IReadOnlyList<QuestStep> Steps);

public sealed class QuestCatalog
{
    public const int SupportedSchema = 5;
    public IReadOnlyDictionary<string, QuestGuide> Guides { get; }

    public QuestCatalog(IEnumerable<QuestGuide> guides) => Guides =
        new ReadOnlyDictionary<string, QuestGuide>(guides.ToDictionary(q => q.Key, StringComparer.Ordinal));

    // Classification is provisional source evidence; a level filter keeps unknown levels visible.
    public IReadOnlyList<QuestGuide> Search(string? text = null, string? category = null, int? level = null) =>
        Guides.Values.Where(q => (string.IsNullOrWhiteSpace(text) || q.Title.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))
            && (category is null || q.Category == category)
            && (level is null || q.BaseLevel is null || q.BaseLevel <= level))
            .OrderBy(q => q.Title, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
}
