using RoAura.Models;

namespace RoAura.Services;

public readonly record struct EffectTransition(TrackedEffect Effect, bool Active);

/// <summary>
/// One status timeline drives both Effects and Reminders, including reminders
/// with no matching effect row. UI visibility and configured rows do not own it.
/// All calls are on the dispatcher, with explicit time for deterministic tests.
/// </summary>
public sealed class StatusTrackingController
{
    private sealed record Status(RoStatusPacket Packet, DateTime ObservedAt, DateTime? EndsAt);
    private sealed class EffectState(int statusId)
    {
        public int StatusId = statusId;
        public bool? Present;
        public DateTime LastAppeared = DateTime.MinValue;
        public DateTime LastDisappeared = DateTime.MinValue;
    }
    private readonly Dictionary<ushort, Status> _statuses = [];
    private readonly Dictionary<Guid, EffectState> _effects = [];

    public void Observe(RoStatusPacket packet, DateTime observedAtUtc)
    {
        if (_statuses.TryGetValue(packet.StatusId, out var old) && old.ObservedAt > observedAtUtc)
            return;
        DateTime? end = packet.Active && packet.RemainingMs is > 0 and < uint.MaxValue
            ? observedAtUtc.AddMilliseconds(packet.RemainingMs) : null;
        // A repeated option flag confirms Hiding but supplies no new timer.
        if (packet.Active && packet.PacketId is 0x0119 or 0x0229 && old?.Packet.Active == true)
        {
            end = old.EndsAt;
            packet = packet with { TotalMs = old.Packet.TotalMs };
        }
        _statuses[packet.StatusId] = new(packet, observedAtUtc, end);
    }

    public IReadOnlyList<EffectTransition> Synchronize(IEnumerable<TrackedEffect> effects,
        IEnumerable<ReminderItem> reminders, DateTime now, string language, bool notificationsEnabled = true)
    {
        var transitions = new List<EffectTransition>();
        var rows = effects.ToList();
        var ids = rows.Select(e => e.Id).ToHashSet();
        foreach (var deleted in _effects.Keys.Where(id => !ids.Contains(id)).ToList()) _effects.Remove(deleted);
        foreach (var reminder in reminders)
            reminder.IsStatusPresent = IsPresent(reminder.StatusId, now);
        foreach (var effect in rows)
        {
            var present = IsPresent(effect.StatusId, now);
            var status = GetStatus(effect.StatusId);
            effect.IsActive = present;
            effect.DurationEndsAtUtc = present ? status?.EndsAt : null;
            effect.RemainingSeconds = present && status?.EndsAt is { } end
                ? Math.Max(0, (end - now).TotalSeconds) : 0;
            effect.TotalDurationSeconds = present && status?.EndsAt is not null
                ? (status.Packet.TotalMs is > 0 and < uint.MaxValue
                    ? status.Packet.TotalMs : status.Packet.RemainingMs) / 1000d : 0;
            var duration = effect.RemainingSeconds > 0
                ? $" · {effect.RemainingSeconds:0.#} {(language == "en" ? "sec" : "с")}" : "";
            effect.CurrentStateText = present
                ? (language == "en" ? $"active{duration}" : $"активен{duration}")
                : (language == "en" ? "missing" : "отсутствует");

            if (!_effects.TryGetValue(effect.Id, out var state) || state.StatusId != effect.StatusId)
                _effects[effect.Id] = state = new(effect.StatusId);
            var previous = state.Present;
            state.Present = present;
            if (previous == present || !notificationsEnabled || !effect.Enabled) continue;
            if (present && effect.NotifyOnAppeared &&
                now - state.LastAppeared >= TimeSpan.FromSeconds(effect.RepeatProtectionSeconds))
            {
                state.LastAppeared = now;
                transitions.Add(new(effect, true));
            }
            else if (!present && previous == true && effect.NotifyOnDisappeared &&
                now - state.LastDisappeared >= TimeSpan.FromSeconds(effect.RepeatProtectionSeconds))
            {
                state.LastDisappeared = now;
                transitions.Add(new(effect, false));
            }
        }
        return transitions;
    }

    public bool IsPresent(int statusId, DateTime now) => GetStatus(statusId) is { } status &&
        status.Packet.Active && (status.EndsAt is null || status.EndsAt > now);

    private Status? GetStatus(int id) => id is > 0 and <= ushort.MaxValue
        ? _statuses.GetValueOrDefault((ushort)id) : null;

    public void Reset()
    {
        _statuses.Clear();
        _effects.Clear();
    }
}
