// Adapted from RankFTW/RHI GameDetectionService.Steam.cs (GPL-3.0),
// blob 1eab65dfcafb1f15e71476b1a88ecf566f1e85f8. Registry lookup stays in the Windows adapter.
using System.Text;
using System.Text.RegularExpressions;
using Rhi.Core.Deployment;

namespace Rhi.Core.Discovery;

public sealed record DiscoveredGame(string Name, string InstallRoot, string Source, string? SuggestedDeploymentRoot = null);
public sealed record DiscoveryResult(DiscoveredGame[] Games, string[] Warnings);

public static class SteamDiscovery
{
    public static DiscoveryResult Scan(string steamPath)
    {
        var games = new List<DiscoveredGame>();
        var warnings = new List<string>();
        var libraries = new HashSet<string>(PathPolicy.Comparer) { PathPolicy.Full(steamPath) };
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            PathPolicy.CheckNoLinks(vdf);
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf, Encoding.UTF8), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        }
        foreach (var library in libraries)
        {
            try
            {
                var apps = Path.Combine(PathPolicy.Full(library), "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var file in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    try
                    {
                        PathPolicy.CheckNoLinks(file);
                        var text = File.ReadAllText(file, Encoding.UTF8);
                        var name = Value(text, "name"); var folder = Value(text, "installdir");
                        if (name is null || folder is null) continue;
                        var root = PathPolicy.Target(Path.Combine(apps, "common"), folder);
                        if (Directory.Exists(root)) games.Add(new(name, root, "Steam"));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Rhi.Core.Models.DeploymentException)
                    { warnings.Add($"{file}: {ex.Message}"); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Rhi.Core.Models.DeploymentException)
            { warnings.Add($"{library}: {ex.Message}"); }
        }
        return new(games.DistinctBy(g => g.InstallRoot, PathPolicy.Comparer).ToArray(), warnings.ToArray());
    }

    private static string? Value(string text, string key)
    {
        var match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }
}
