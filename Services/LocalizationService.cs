using System.Globalization;
using System.Windows;

namespace RoAura.Services;

public static class LocalizationService
{
    private const string Prefix = "Resources/Strings.";
    public static string CurrentLanguage { get; private set; } = "ru";

    public static void ApplyLanguage(string? language)
    {
        var code = language == "en" ? "en" : "ru";
        CurrentLanguage = code;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var old = dictionaries.FirstOrDefault(item =>
            item.Source?.OriginalString.Contains(Prefix, StringComparison.OrdinalIgnoreCase) == true);
        if (old is not null) dictionaries.Remove(old);
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"{Prefix}{code}.xaml", UriKind.Relative)
        });
        var uiCulture = CultureInfo.GetCultureInfo(code == "en" ? "en-US" : "ru-RU");
        CultureInfo.CurrentUICulture = uiCulture;
        // Background threads (packet capture, market refresh, timers) must
        // format and localize exactly like the UI thread: the UI culture
        // follows the selected language, number/date formatting keeps the
        // UI thread's culture so all threads agree.
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
    }

    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
