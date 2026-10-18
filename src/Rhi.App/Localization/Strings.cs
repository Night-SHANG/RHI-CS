using System.Globalization;
using System.Text.Json;

namespace Rhi.App.Localization;

public static class Strings
{
    private static Dictionary<string, string> values = [];
    public static string Language { get; private set; } = "System";
    public static void Use(string language)
    {
        Language = language;
        var chinese = language == "zh-CN" || language == "System" &&
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
        var path = Path.Combine(AppContext.BaseDirectory, "Localization", chinese ? "zh-CN.json" : "en-US.json");
        values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, System.Text.Encoding.UTF8))!;
    }
    public static string Get(string key) => values.TryGetValue(key, out var value) ? value : key;
    public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
