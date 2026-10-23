namespace RoAura.Models;

public static class GameServerProfile
{
    public const string Rtm = "RTM";
    public const string SpringRo = "SpringRO";
    public const string Other = "Other";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "springro" => SpringRo,
        "other" or "другой" => Other,
        _ => Rtm
    };

    public static bool SupportsServerDatabases(string? value) =>
        Normalize(value) is Rtm or SpringRo;

    public static bool SupportsDatabaseOverlay(string? value) =>
        Normalize(value) == SpringRo;
}
