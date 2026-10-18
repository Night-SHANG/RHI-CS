using Rhi.App.Localization;
using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.App.Presentation;

public static class ResourcePresentation
{
    public static string KindName(ResourceKind kind) => Strings.Get(kind.ToString());
    public static string VersionName(ResourceVersion version) => version.Kind == ResourceKind.Files
        ? version.Label : version.Label + " · " + KindName(version.Kind);
    public static string TargetLocation(string folder) => string.IsNullOrWhiteSpace(folder)
        ? Strings.Get("GameDeploymentFolder") : Strings.Format("GameDeploymentSubfolder", folder);
    public static string VersionDestination(string value, string libraryFolder)
    {
        try
        {
            var folder = PathPolicy.Relative(value);
            return Strings.Format(folder.Length == 0 ? "VersionTargetHelp" : "VersionTargetLocation",
                TargetLocation(folder.Length == 0 ? libraryFolder : folder));
        }
        catch (DeploymentException) { return ""; } // Invalid input is explained by the shared form validator.
    }
    public static bool SourceAvailable(ResourceVersion version) => version.Kind switch
    {
        ResourceKind.Files => File.Exists(version.SourcePath) || Directory.Exists(version.SourcePath),
        ResourceKind.ReShadeStandard or ResourceKind.ReShadeAddon => File.Exists(version.SourcePath) &&
            (Path.GetExtension(version.SourcePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(version.SourcePath).Equals(".dll", StringComparison.OrdinalIgnoreCase)),
        _ => false
    };
}
