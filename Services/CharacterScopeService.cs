using RoAura.Models;

namespace RoAura.Services;

public static class CharacterScopeService
{
    public static bool Discover(AppSettings settings, IEnumerable<CharacterStatisticsKey> keys)
    {
        var changed = false;
        foreach (var key in keys)
        {
            if (key.CharacterId == 0 || settings.Characters.Any(c => c.CharacterId == key.CharacterId &&
                    string.Equals(c.DetectedServer, key.ServerKey, StringComparison.OrdinalIgnoreCase))) continue;
            var identity = System.Text.Encoding.UTF8.GetBytes(key.ServerKey.ToUpperInvariant() + ":" + key.CharacterId);
            var id = new Guid(System.Security.Cryptography.SHA256.HashData(identity).AsSpan(0, 16));
            settings.Characters.Add(new CharacterProfile { Id = id, CharacterId = key.CharacterId, DetectedServer = key.ServerKey,
                ServerProfile = key.ServerKey });
            changed = true;
        }
        return changed;
    }

    public static bool Allows(CharacterScope scope, IEnumerable<CharacterProfile> profiles, CharacterSessionSnapshot session)
    {
        if (scope.Characters.Count == 0) return true; // legacy/all-character overlays
        if (session.Phase != CharacterSessionPhase.ActiveObserved || session.CharacterId is not > 0) return false;
        return profiles.Any(c => scope.Characters.Contains(c.Id) && c.CharacterId == session.CharacterId &&
            string.Equals(c.ServerProfile, session.ServerKey, StringComparison.OrdinalIgnoreCase));
    }

    public static void Refresh(AppSettings settings, CharacterSessionSnapshot session)
    {
        var scopes = settings.Skills.Select(x => x.CharacterScope).Concat(settings.Effects.Select(x => x.CharacterScope))
            .Concat(settings.Reminders.Select(x => x.CharacterScope)).Concat(settings.Equipment.Select(x => x.CharacterScope))
            .Concat(settings.BuffPanelGroups.Select(x => x.CharacterScope));
        if (!string.Equals(settings.ServerProfile, session.ServerKey, StringComparison.OrdinalIgnoreCase) || !settings.PacketCaptureEnabled)
            session = default;
        foreach (var scope in scopes) scope.IsAllowed = Allows(scope, settings.Characters, session);
    }
}
