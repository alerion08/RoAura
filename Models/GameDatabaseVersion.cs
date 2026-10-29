namespace RoAura.Models;

public static class GameDatabaseVersion
{
    public const string Rtm = "RTM";
    public const string SpringRo = "SpringRO";

    public const string Toc = "ToC";

    public static string Normalize(string? value) =>
        string.Equals(value, Toc, StringComparison.OrdinalIgnoreCase) ? Toc :
        string.Equals(value, SpringRo, StringComparison.OrdinalIgnoreCase) ? SpringRo : Rtm;
}
