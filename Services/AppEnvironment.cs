namespace RoAura.Services;

/// <summary>Separate the development portable identity from the public release at compile time.</summary>
public static class AppEnvironment
{
    // Character-bound statistics are part of the regular product since 3.1.1.
    public const bool CharacterSessionsEnabled = true;
#if ROAURA_DEV
    public const bool IsDevelopment = true;
    public const string DataFolderName = "RoAura-Dev";
    public const string SingleInstanceMutexName = @"Local\RoAura.Dev.SingleInstance";
    public const string ActivateEventName = @"Local\RoAura.Dev.Activate";
#else
    public const bool IsDevelopment = false;
    public const string DataFolderName = "RoAura";
    public const string SingleInstanceMutexName = @"Local\RoAura.SingleInstance";
    public const string ActivateEventName = @"Local\RoAura.Activate";
#endif
    public static string RoamingDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DataFolderName);
    public static string LocalDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);
}
