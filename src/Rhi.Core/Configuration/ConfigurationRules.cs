using Rhi.Core.Deployment;
using Rhi.Core.Discovery;
using Rhi.Core.Models;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Configuration;

public static class ConfigurationRules
{
    public static string Name(string value) => string.IsNullOrWhiteSpace(value)
        ? throw new DeploymentException("NameRequired") : value.Trim();

    public static ResourceLibrary Library(ResourceLibrary value) => value with
    { Name = Name(value.Name), TargetRelative = TargetFolder(value.TargetRelative) };

    private static string TargetFolder(string value)
    {
        if (Path.IsPathRooted(value)) throw new DeploymentException("TargetFolderAbsolute", value);
        try { return PathPolicy.Relative(value); }
        catch (DeploymentException ex) when (ex.Code == "UnsafeRelativePath")
        { throw new DeploymentException("TargetFolderInvalid", value); }
    }

    private static string SourcePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DeploymentException("SourcePathRequired");
        try { return PathPolicy.Full(value); }
        catch (DeploymentException ex) when (ex.Code == "LocalAbsolutePathRequired")
        { throw new DeploymentException("SourcePathInvalid", value); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new DeploymentException("SourcePathInvalid", value); }
    }

    public static ResourceVersion Version(WorkspaceState state, string dataRoot, ResourceVersion value)
    {
        var name = Name(value.Label);
        var source = SourcePath(value.SourcePath);
        if (!File.Exists(source) && !Directory.Exists(source)) throw new DeploymentException("SourcePathMissing", source);
        if (!Enum.IsDefined(value.Kind)) throw new DeploymentException("ReShadeInvalidBinary");
        if (value.Kind != ResourceKind.Files && (!File.Exists(source) ||
            !(Path.GetExtension(source).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
              Path.GetExtension(source).Equals(".dll", StringComparison.OrdinalIgnoreCase))))
            throw new DeploymentException("ReShadeInvalidBinary", source);
        try { PathPolicy.Disjoint(source, dataRoot); }
        catch (DeploymentException ex) when (ex.Code == "OverlappingPaths")
        { throw new DeploymentException("SourceOverlapsData", source); }
        try
        {
            foreach (var game in state.Games)
            { PathPolicy.DisjointSource(game.InstallRoot, source); PathPolicy.DisjointSource(game.DeploymentRoot, source); }
        }
        catch (DeploymentException ex) when (ex.Code == "OverlappingPaths")
        { throw new DeploymentException("SourceOverlapsGame", source); }
        return value with { Label = name, SourcePath = source, TargetOverride = value.Kind != ResourceKind.Files ? "" :
            string.IsNullOrWhiteSpace(value.TargetOverride) ? null : TargetFolder(value.TargetOverride) };
    }

    public static GameEntry Game(WorkspaceState state, string dataRoot, GameEntry value, bool relocation = false)
    {
        var name = Name(value.Name);
        var exe = PathPolicy.Full(value.ExePath);
        var install = PathPolicy.Full(value.InstallRoot);
        var target = PathPolicy.Full(value.DeploymentRoot);
        if (!File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(install) || !Directory.Exists(target) ||
            !PathPolicy.Within(DirectoryLease.PhysicalPath(exe), DirectoryLease.PhysicalPath(install)))
            throw new DeploymentException("GameUnavailable", exe);
        var architecture = PeInspector.Architecture(exe);
        if (architecture == "Unknown") throw new DeploymentException("InvalidExecutable", exe);
        var old = state.Games.SingleOrDefault(g => g.Id == value.Id);
        if (!relocation && old is not null && state.Receipts.Any(r => r.GameId == old.Id) &&
            (!PathPolicy.Comparer.Equals(old.ExePath, exe) || !PathPolicy.Comparer.Equals(old.InstallRoot, install) ||
             !PathPolicy.Comparer.Equals(old.DeploymentRoot, target))) throw new DeploymentException("ManagedLocationLocked");
        PathPolicy.Disjoint(install, dataRoot); PathPolicy.Disjoint(target, dataRoot);
        foreach (var other in state.Games.Where(g => g.Id != value.Id)) PathPolicy.DisjointSource(other.DeploymentRoot, target);
        foreach (var source in state.Libraries.SelectMany(l => l.Versions).Select(v => v.SourcePath))
        { PathPolicy.DisjointSource(source, install); PathPolicy.DisjointSource(source, target); }
        return value with { Name = name, ExePath = exe, InstallRoot = install, DeploymentRoot = target, Architecture = architecture };
    }
}
