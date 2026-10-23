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
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(code == "en" ? "en-US" : "ru-RU");
    }

    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
