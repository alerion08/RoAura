namespace RoAura.Models;

public static class GameDatabaseVersion
{
    public const string Rtm = "RTM";
    public const string SpringRo = "SpringRO";

    public static string Normalize(string? value) =>
        string.Equals(value, SpringRo, StringComparison.OrdinalIgnoreCase) ? SpringRo : Rtm;
}
