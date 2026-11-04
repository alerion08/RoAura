using RoAura.Models;

namespace RoAura.Services;

/// <summary>Shared session boundaries and income calculation for statistics and Gold Goblin.</summary>
public static class FarmingSessionActions
{
    public static void Restart(CharacterStatisticsLedger ledger, GoldGoblinService? gold,
        AppSettings settings, DateTime nowUtc, bool startImmediately)
    {
        ledger.ResetCurrentSession(nowUtc, startImmediately);
        settings.SessionPaused = false;
        if (startImmediately) gold?.StartSession();
        else gold?.Reset();
    }

    public static long CalculateIncome(AppSettings settings, long directZeny, long lootValue)
    {
        var supportsGold = GameServerProfile.SupportsGoldGoblin(settings.ServerProfile);
        return (supportsGold && settings.GoldGoblinIgnoreZenyIncome ? 0 : directZeny) +
            (supportsGold && settings.IncludeGoldGoblinInZenyPerHour ? lootValue : 0);
    }
}
