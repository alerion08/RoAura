using System.Security.Cryptography;
using System.Text;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>An explicit identity, never inferred from AID, last login, preset or TCP port.</summary>
public sealed record QuestProfileKey
{
    public string Server { get; }
    public uint? CharacterId { get; }
    public Guid? LocalProfileId { get; }
    public string StorageId => CharacterId is { } cid ? "cid-" + cid : "local-" + LocalProfileId!.Value.ToString("N");
    public QuestProfileKey(string server, uint? characterId, Guid? localProfileId)
    {
        Server = (server ?? "").Trim().ToUpperInvariant();
        if (Server.Length is < 1 or > 48 || Server.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')) ||
            (characterId.HasValue == localProfileId.HasValue) || characterId == 0 || localProfileId == Guid.Empty)
            throw new ArgumentException("Specify a server and exactly one nonzero CID or explicit local profile.");
        CharacterId = characterId;
        LocalProfileId = localProfileId;
    }

    public static QuestProfileKey? FromObserved(CharacterSessionSnapshot session, string selectedServer, bool packetsEnabled) =>
        packetsEnabled && session.Phase == CharacterSessionPhase.ActiveObserved && session.CharacterId is > 0 &&
        string.Equals(session.ServerKey, selectedServer, StringComparison.OrdinalIgnoreCase)
            ? new QuestProfileKey(session.ServerKey, session.CharacterId, null) : null;
}

public enum QuestManualState { Active, Paused, Completed, Removed }
public sealed record QuestHistoryEntry(DateTime AtUtc, QuestManualState State, string Method = "manual");
public sealed class QuestProgressEntry
{
    public string QuestKey { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsCollapsed {get;set;}
    public List<string> CompletedStepAnchors {get;set;}=[];
    public int CurrentStep { get; set; }
    public string? StepAnchor { get; set; }
    public string? CatalogContentHash { get; set; }
    public bool IsVisibleInOverlay { get; set; } = true;
    public int Order { get; set; }
    public QuestManualState State { get; set; }
    public bool NeedsStepReview { get; set; }
    public bool IsMissingFromCatalog { get; set; }
    public List<QuestHistoryEntry> History { get; set; } = [];
}

public sealed class QuestProgressDocument
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Server { get; set; } = "";
    public uint? CharacterId { get; set; }
    public Guid? LocalProfileId { get; set; }
    public List<QuestProgressEntry> Quests { get; set; } = [];
    public static QuestProgressDocument Create(QuestProfileKey key) => new()
        { Server = key.Server, CharacterId = key.CharacterId, LocalProfileId = key.LocalProfileId };
}

/// <summary>Manual instructions only. Navigation does not complete; explicit Done confirms the current instruction.</summary>
public static class QuestProgress
{
    public static string Anchor(QuestStep step) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        (step.Kind ?? "") + "\n" + step.Instruction)));

    public static string CompletionKey(QuestStep step)=>step.Number+":"+Anchor(step);
    public static bool ValidCompletionKey(string value)
    {
        var parts=value.Split(':');
        return parts.Length==2 && int.TryParse(parts[0],out var number) && number>0 && parts[1].Length==64 && parts[1].All(Uri.IsHexDigit);
    }
    public static QuestProgressEntry Add(QuestProgressDocument document, QuestGuide guide)
    {
        var existing = document.Quests.FirstOrDefault(q => q.QuestKey == guide.Key);
        if (existing is not null) return existing; // history restoration is an explicit action
        if (guide.Steps.Count == 0) throw new InvalidOperationException("This guide has no instructions.");
        var entry = new QuestProgressEntry { QuestKey = guide.Key, Title = guide.Title,
            Order = document.Quests.Count == 0 ? 0 : checked(document.Quests.Max(q => q.Order) + 1) };
        SelectStep(entry, guide, guide.Steps[0].Number);
        document.Quests.Add(entry);
        return entry;
    }

    public static void SelectStep(QuestProgressEntry entry, QuestGuide guide, int stepNumber)
    {
        RequireSameQuest(entry, guide);
        var step = guide.Steps.SingleOrDefault(s => s.Number == stepNumber)
            ?? throw new ArgumentOutOfRangeException(nameof(stepNumber));
        entry.CurrentStep = step.Number;
        entry.StepAnchor = Anchor(step);
        entry.CatalogContentHash = guide.ContentHash;
        entry.NeedsStepReview = entry.IsMissingFromCatalog = false;
    }

    public static bool Move(QuestProgressEntry entry, QuestGuide guide, int direction)
    {
        RequireSameQuest(entry, guide);
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (entry.NeedsStepReview || entry.IsMissingFromCatalog) return false;
        var index = guide.Steps.ToList().FindIndex(s => s.Number == entry.CurrentStep);
        if (index < 0 || index + direction < 0 || index + direction >= guide.Steps.Count) return false;
        SelectStep(entry, guide, guide.Steps[index + direction].Number);
        return true;
    }

    public static bool ConfirmStep(QuestProgressEntry entry, QuestGuide guide, int expectedStep, DateTime atUtc)
    {
        RequireSameQuest(entry, guide);
        if (atUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.");
        if (entry.State != QuestManualState.Active || entry.NeedsStepReview || entry.IsMissingFromCatalog || entry.CurrentStep != expectedStep) return false;
        var index = guide.Steps.ToList().FindIndex(s => s.Number == expectedStep);
        if (index < 0) return false;
        var anchor=CompletionKey(guide.Steps[index]);if(!entry.CompletedStepAnchors.Contains(anchor))entry.CompletedStepAnchors.Add(anchor);
        if (index + 1 < guide.Steps.Count) SelectStep(entry, guide, guide.Steps[index + 1].Number);
        else SetState(entry, QuestManualState.Completed, atUtc, true);
        return true;
    }

    public static void SetState(QuestProgressEntry entry, QuestManualState state, DateTime atUtc, bool completionConfirmed = false)
    {
        if (!Enum.IsDefined(state) || atUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid manual state or UTC time.");
        if (state == QuestManualState.Completed && !completionConfirmed)
            throw new InvalidOperationException("Manual completion requires confirmation.");
        if (entry.State == state) return;
        entry.State = state;
        entry.History.Add(new QuestHistoryEntry(atUtc, state));
    }

    public static void Reconcile(QuestProgressDocument document, QuestCatalog catalog)
    {
        foreach (var entry in document.Quests)
        {
            if (!catalog.Guides.TryGetValue(entry.QuestKey, out var guide))
            {
                entry.IsMissingFromCatalog = entry.NeedsStepReview = true;
                continue; // retain current position, anchor and history
            }
            entry.IsMissingFromCatalog = false;
            var unchanged = guide.ContentHash is not null && guide.ContentHash == entry.CatalogContentHash &&
                guide.Steps.Any(s => s.Number == entry.CurrentStep && Anchor(s) == entry.StepAnchor);
            if (unchanged && !entry.NeedsStepReview) continue;
            var matches = guide.Steps.Where(s => Anchor(s) == entry.StepAnchor).ToList();
            // Even a matching numeric position is unsafe if its instruction changed.
            if (matches.Count == 1) SelectStep(entry, guide, matches[0].Number);
            else entry.NeedsStepReview = true;
        }
    }

    private static void RequireSameQuest(QuestProgressEntry entry, QuestGuide guide)
    {
        if (entry.QuestKey != guide.Key) throw new ArgumentException("Quest identity mismatch.");
    }
}
