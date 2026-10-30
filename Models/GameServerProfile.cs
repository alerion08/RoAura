namespace RoAura.Models;

public static class GameServerProfile
{
    public const string Rtm = "RTM";
    public const string SpringRo = "SpringRO";
    public const string Toc = "ToC";
    public const string Other = "Other";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "springro" => SpringRo,
        "toc" or "tides of comodo" => Toc,
        "other" or "другой" => Other,
        _ => Rtm
    };

    public static bool SupportsServerDatabases(string? value) =>
        Normalize(value) is Rtm or SpringRo or Toc;

    public static bool SupportsDatabaseOverlay(string? value) =>
        Normalize(value) is SpringRo or Toc;
    public static bool SupportsGoldGoblin(string? value) => Normalize(value) is SpringRo or Toc;
    public static bool SupportsMarket(string? value) => Normalize(value) == SpringRo;
}
