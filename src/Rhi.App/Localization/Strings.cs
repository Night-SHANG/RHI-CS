using System.Globalization;
using Rhi.App.Vendor.RhiCn;

namespace Rhi.App.Localization;

public static class Strings
{
    public static Task InitializeAsync(string preference) => LocalizationService.InitializeAsync(preference);
    public static Task UseAsync(string preference) => LocalizationService.SetLanguageAsync(preference);
    public static string[] AvailableLanguages => LocalizationService.AvailableLanguages;
    public static string Get(string key) => LocalizationService.GetString(key, key);
    public static string Format(string key, params object[] args) => LocalizationService.Format(key, key, args);
    public static string LanguageName(string language)
    {
        var translated = Get(language);
        if (translated != language) return translated;
        try { return CultureInfo.GetCultureInfo(language).NativeName; }
        catch (CultureNotFoundException) { return language; }
    }
}
