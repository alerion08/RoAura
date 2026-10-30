using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>Copies the serialized configuration while preserving the objects observed by running services.</summary>
public static class SettingsSnapshot
{
    public static void Apply(AppSettings source, AppSettings target)
    {
        CopyObject(source, target);
        target.SyncCooldownGroups();
        target.SyncBuffPanelGroups();
        target.SyncRewardGroups();
        target.SyncRateIndicators();
        target.ActiveNotifications.Clear();
        target.ActiveMarketNotifications.Clear();
        target.NotifySettingsApplied();
    }

    private static void CopyObject(object source, object target)
    {
        foreach (var property in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.SetMethod?.IsPublic != true ||
                property.GetIndexParameters().Length != 0 || property.IsDefined(typeof(JsonIgnoreAttribute))) continue;
            var incoming = property.GetValue(source);
            var current = property.GetValue(target);
            if (source is AppSettings && property.Name == nameof(AppSettings.Characters) &&
                incoming is IEnumerable<CharacterProfile> characters && current is IList knownCharacters)
            {
                // Names/server assignments are a global registry, not destructive preset state.
                foreach (var character in characters)
                    if (!knownCharacters.Cast<CharacterProfile>().Any(c => c.Id == character.Id ||
                        (c.CharacterId == character.CharacterId && string.Equals(c.DetectedServer, character.DetectedServer, StringComparison.OrdinalIgnoreCase))))
                        knownCharacters.Add(character);
            }
            else if (incoming is IList items && current is IList existing)
            {
                // Remove individually: Reset carries no OldItems for subscribers that detach item handlers.
                for (var i = existing.Count - 1; i >= 0; i--) existing.RemoveAt(i);
                foreach (var item in items) existing.Add(item);
            }
            else if (incoming is not null && current is not null &&
                     property.PropertyType.Namespace == typeof(AppSettings).Namespace && property.PropertyType.IsClass)
                CopyObject(incoming, current);
            else property.SetValue(target, incoming);
        }
    }
}
