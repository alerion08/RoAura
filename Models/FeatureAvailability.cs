namespace RoAura.Models;

public enum AppFeature { Home, Cooldowns, Effects, Reminders, Equipment, MvpTimers, Statistics, GoldGoblin, Database }

public static class FeatureAvailability
{
    public static bool RequiresPackets(AppFeature feature) => feature is AppFeature.Cooldowns or AppFeature.Effects
        or AppFeature.Reminders or AppFeature.Equipment or AppFeature.Statistics or AppFeature.GoldGoblin;
    public static bool CanUse(string? server, bool packets, AppFeature feature) =>
        (!RequiresPackets(feature) || packets) && (feature switch
        {
            AppFeature.MvpTimers or AppFeature.Statistics => GameServerProfile.SupportsServerDatabases(server),
            AppFeature.GoldGoblin => GameServerProfile.SupportsGoldGoblin(server),
            AppFeature.Database => GameServerProfile.SupportsDatabaseOverlay(server),
            _ => true
        });
}
