using System.Text;
using ReShade.Setup.Utilities;
using Rhi.Core.Models;

namespace Rhi.Core.Deployment.Resources.ReShade;

internal static class ReShadeConfiguration
{
    internal const int MaximumBytes = 4 * 1024 * 1024;
    internal sealed record Prepared(byte[] Content, ConfigurationEdit[] Edits);

    public static Prepared Prepare(byte[]? original, string targetRoot)
    {
        try
        {
            var bytes = original ?? [];
            using var textInput = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(textInput, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            ValidateSyntax(reader.ReadToEnd());
            var ini = new IniFile(new MemoryStream(bytes, writable: false));
            var edits = new List<ConfigurationEdit>();
            foreach (var (key, fallback) in new[]
            {
                ("EffectSearchPaths", @".\reshade-shaders\Shaders\**"),
                ("TextureSearchPaths", @".\reshade-shaders\Textures\**")
            })
            {
                var existing = ini.GetString("GENERAL", key);
                if (existing is null)
                {
                    ini.SetValue("GENERAL", key, fallback);
                    edits.Add(new("GENERAL", key, null, fallback));
                }
                else
                {
                    // Existing choices, including an intentionally empty list, are preserved.
                    ini.GetValue("GENERAL", key, out var paths);
                    foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
                        ValidateSearchPath(path, targetRoot);
                }
            }
            return new(edits.Count == 0 ? bytes : new UTF8Encoding(false).GetBytes(ini.Serialize()), edits.ToArray());
        }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or ArgumentException or DeploymentException)
        { throw new DeploymentException("ReShadeInvalidIni", ex.Message); }
    }

    private static void ValidateSearchPath(string path, string root)
    {
        var directory = path.Trim();
        if (directory.EndsWith("\\**", StringComparison.Ordinal) || directory.EndsWith("/**", StringComparison.Ordinal))
            directory = directory[..^3];
        if (directory.IndexOfAny(['*', '?', '%', '\0']) >= 0) throw new InvalidDataException(path);
        var full = Path.GetFullPath(directory, root);
        if (!PathPolicy.Within(full, root)) throw new InvalidDataException(path);
        PathPolicy.CheckNoLinks(full);
    }

    // Guard only: IniFile remains the parser and serializer. Reject inputs for which its
    // permissive duplicate/malformed-line handling could silently discard user settings.
    private static void ValidateSyntax(string text)
    {
        if (text.Contains('\uFFFD')) throw new InvalidDataException("Unreliable configuration encoding.");
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Contains('\0')) throw new InvalidDataException("NUL in configuration.");
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line.StartsWith('['))
            {
                if (!line.EndsWith(']') || line.Length < 3 || line[1..^1].IndexOfAny(['[', ']']) >= 0)
                    throw new InvalidDataException("Malformed section.");
                section = line[1..^1];
                if (section != section.Trim() || !sections.Add(section) ||
                    section.Equals("GENERAL", StringComparison.OrdinalIgnoreCase) && section != "GENERAL")
                    throw new InvalidDataException("Ambiguous section.");
                continue;
            }
            var split = line.IndexOf('=');
            if (split <= 0) throw new InvalidDataException("Malformed key.");
            var key = line[..split].Trim();
            if (!keys.Add(section + "\0" + key)) throw new InvalidDataException("Duplicate key.");
            if (section == "GENERAL" && new[] { "EffectSearchPaths", "TextureSearchPaths" }.Any(k =>
                k.Equals(key, StringComparison.OrdinalIgnoreCase) && k != key))
                throw new InvalidDataException("Ambiguous search-path key.");
        }
    }
}
