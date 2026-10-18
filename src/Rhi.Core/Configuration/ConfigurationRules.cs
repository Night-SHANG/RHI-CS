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
    { Name = Name(value.Name), TargetRelative = PathPolicy.Relative(value.TargetRelative) };

    public static ResourceVersion Version(WorkspaceState state, string dataRoot, ResourceVersion value)
    {
        var name = Name(value.Label);
        var source = PathPolicy.Full(value.SourcePath);
        if (!File.Exists(source) && !Directory.Exists(source)) throw new DeploymentException("SourceUnavailable", source);
        if (!Enum.IsDefined(value.Kind)) throw new DeploymentException("ReShadeInvalidBinary");
        if (value.Kind != ResourceKind.Files && (!File.Exists(source) ||
            !(Path.GetExtension(source).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
              Path.GetExtension(source).Equals(".dll", StringComparison.OrdinalIgnoreCase))))
            throw new DeploymentException("ReShadeInvalidBinary", source);
        PathPolicy.Disjoint(source, dataRoot);
        foreach (var game in state.Games)
        { PathPolicy.DisjointSource(game.InstallRoot, source); PathPolicy.DisjointSource(game.DeploymentRoot, source); }
        return value with { Label = name, SourcePath = source, TargetOverride = value.Kind != ResourceKind.Files ? "" :
            string.IsNullOrWhiteSpace(value.TargetOverride) ? null : PathPolicy.Relative(value.TargetOverride) };
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
