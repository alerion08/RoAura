namespace RoAura.Models;

public static class GameServerProfile
{
    public const string Rtm = "RTM";
    public const string SpringRo = "SpringRO";
    public const string Toc = "ToC";
    public const string MuhRo = "MuhRO";
    public const string RagnarokPlus = "Ragnarok+";
    public const string Other = "Other";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "springro" => SpringRo,
        "toc" or "tides of comodo" => Toc,
        "muhro" or "muh" => MuhRo,
        "ragnarok+" or "ragnarokplus" or "ragnarok plus" => RagnarokPlus,
        "other" or "другой" => Other,
        _ => Rtm
    };

    public static bool SupportsServerDatabases(string? value) =>
        Normalize(value) is Rtm or SpringRo or Toc;

    public static bool SupportsDatabaseOverlay(string? value) =>
        Normalize(value) is SpringRo or Toc;
    public static bool SupportsGoldGoblin(string? value) => Normalize(value) is SpringRo or Toc;
    public static bool SupportsMarket(string? value) => Normalize(value) == SpringRo;

    // The decoded-memory bridge is specific to the analyzed SpringRO client.
    // MuhRO's observed status packets already contain the standard fields.
    public static bool UsesSpringRoClientStatus(string? value) => Normalize(value) == SpringRo;

    public static void EnsureClientProcess(AppSettings settings)
    {
        var process = Normalize(settings.ServerProfile) switch
        {
            MuhRo => "Muh",
            RagnarokPlus => "RagnarokPlus",
            _ => null
        };
        if (process is not null && !settings.AllowedProcesses.Any(name =>
                !string.IsNullOrWhiteSpace(name) && string.Equals(System.IO.Path.GetFileNameWithoutExtension(name.Trim()), process, StringComparison.OrdinalIgnoreCase)))
            settings.AllowedProcesses = [.. settings.AllowedProcesses, process];
    }
}
