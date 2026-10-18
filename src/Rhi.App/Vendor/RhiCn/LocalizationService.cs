// Adapted from Night-SHANG/RHI-CN (GPL-3.0), commit 12016e59ab122c030cddbe0dbe973607ffabd966.
// Source: overlay/RenoDXCommander/Services/LocalizationService.cs, blob d46a5453e432cf0dd594a6bdf393b21408cfae3f.
// Keep the upstream LocalizerBuilder/GetString/Format integration. Omit legacy RHI preference files,
// UI converters and hashed data strings. Preferences remain in WorkspaceState; support live switching
// and English fallback through the package's language dictionaries. See root LICENSE.
using System.Globalization;
using WinUI3Localizer;

namespace Rhi.App.Vendor.RhiCn;

public static class LocalizationService
{
    public static string StringsFolderPath => Path.Combine(AppContext.BaseDirectory, "Strings");
    public static string[] AvailableLanguages => Localizer.Get().GetAvailableLanguages()
        .Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static async Task InitializeAsync(string preference)
    {
        _ = await new LocalizerBuilder()
            .AddStringResourcesFolderForLanguageDictionaries(StringsFolderPath)
            .SetOptions(options => options.DefaultLanguage = "en-US")
            .Build();
        await SetLanguageAsync(preference);
    }

    public static Task SetLanguageAsync(string preference)
    {
        var requested = preference.Equals("System", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.CurrentUICulture.Name : preference;
        var available = AvailableLanguages;
        var language = available.FirstOrDefault(l => l.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (language is null && requested.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            language = available.FirstOrDefault(l => l == "zh-CN");
        language ??= available.FirstOrDefault(l => l.Split('-')[0].Equals(requested.Split('-')[0], StringComparison.OrdinalIgnoreCase));
        return Localizer.Get().SetLanguage(language ?? "en-US");
    }

    public static string GetString(string key, string fallback = "")
    {
        var value = Localizer.Get().GetLocalizedString(key);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        var english = Localizer.Get().GetLanguageDictionaries().FirstOrDefault(d => d.Language == "en-US");
        return english?.TryGetItems(key, out var items) == true && items.LastOrDefault() is { } item
            ? item.Value : fallback;
    }

    public static string Format(string key, string fallback, params object?[] args)
    {
        var template = GetString(key, fallback);
        return string.Format(CultureInfo.CurrentCulture, template, args);
    }
}
