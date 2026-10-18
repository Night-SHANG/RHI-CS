using Rhi.App.Vendor.Rhi.Models;
using Rhi.App.Vendor.Rhi.Services;
using Rhi.Core.Deployment;
using Rhi.Core.Discovery;
using Rhi.Core.Storage.Windows;

namespace Rhi.App.Services.Discovery;

public static class RhiGameDiscovery
{
    public static DiscoveryResult Scan()
    {
        var warnings = new List<string>();
        var games = new List<DiscoveredGame>();
        var service = new GameDetectionService(message => System.Diagnostics.Trace.WriteLine(message));
        service.ClearEngineCache();
        try
        {
            var steam = WindowsIntegration.ScanSteam();
            games.AddRange(steam.Games);
            warnings.AddRange(steam.Warnings);
        }
        catch (Exception ex) { warnings.Add($"Steam: {ex.Message}"); }

        (string name, Func<List<DetectedGame>> scan)[] platforms =
        [
            ("GOG", service.FindGogGames), ("Epic", service.FindEpicGames),
            ("EA", service.FindEaGames), ("Xbox", service.FindXboxGames),
            ("Ubisoft", service.FindUbisoftGames), ("Battle.net", service.FindBattleNetGames),
            ("Rockstar", service.FindRockstarGames)
        ];
        foreach (var (name, scan) in platforms)
        {
            try { games.AddRange(scan().Select(g => new DiscoveredGame(g.Name, g.InstallPath, g.Source))); }
            catch (Exception ex) { warnings.Add($"{name}: {ex.Message}"); }
        }
        var seen = new HashSet<string>(PathPolicy.Comparer);
        var result = new List<DiscoveredGame>();
        foreach (var game in games)
        {
            try
            {
                var root = PathPolicy.Full(game.InstallRoot);
                if (!Directory.Exists(root) || !seen.Add(DirectoryLease.PhysicalPath(root))) continue;
                var (suggested, _) = service.DetectEngineAndPath(root);
                suggested = PathPolicy.Full(suggested);
                if (!PathPolicy.Within(suggested, root)) suggested = root;
                result.Add(game with { InstallRoot = root, SuggestedDeploymentRoot = suggested });
            }
            catch (Exception ex) { warnings.Add($"{game.Name}: {ex.Message}"); }
        }
        return new(result.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), warnings.ToArray());
    }
}
