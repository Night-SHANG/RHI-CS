using Rhi.App.Localization;
using Rhi.Core.Models;

namespace Rhi.App.Presentation;

public static class ResourcePresentation
{
    public static string KindName(ResourceKind kind) => Strings.Get(kind.ToString());
    public static string VersionName(ResourceVersion version) => version.Kind == ResourceKind.Files
        ? version.Label : version.Label + " · " + KindName(version.Kind);
    public static string TargetLocation(string folder) => string.IsNullOrWhiteSpace(folder)
        ? Strings.Get("GameDeploymentFolder") : Strings.Format("GameDeploymentSubfolder", folder);
    public static bool SourceAvailable(ResourceVersion version) => version.Kind switch
    {
        ResourceKind.Files => File.Exists(version.SourcePath) || Directory.Exists(version.SourcePath),
        ResourceKind.ReShadeStandard or ResourceKind.ReShadeAddon => File.Exists(version.SourcePath) &&
            (Path.GetExtension(version.SourcePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(version.SourcePath).Equals(".dll", StringComparison.OrdinalIgnoreCase)),
        _ => false
    };
}
