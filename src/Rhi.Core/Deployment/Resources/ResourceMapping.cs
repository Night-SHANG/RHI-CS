using Rhi.Core.Models;

namespace Rhi.Core.Deployment.Resources;

internal sealed record ResourceFile(string? SourcePath, byte[]? Content = null, ConfigurationEdit[]? Edits = null);
internal sealed record ResourceMap(Dictionary<string, ResourceFile> Files, SourceSnapshot[] Inputs);
internal interface IResourceAdapter
{
    ResourceMap Map(ResourceLibrary library, ResourceVersion version, GameEntry game);
}

internal static class ResourceMapping
{
    private static readonly IReadOnlyDictionary<ResourceKind, IResourceAdapter> Adapters =
        new Dictionary<ResourceKind, IResourceAdapter>
        {
            [ResourceKind.Files] = new OrdinaryFiles(),
            [ResourceKind.ReShadeStandard] = new ReShade.ReShadeAdapter(),
            [ResourceKind.ReShadeAddon] = new ReShade.ReShadeAdapter()
        };

    public static ResourceMap Map(ResourceLibrary library, ResourceVersion version, GameEntry game) =>
        Adapters.TryGetValue(version.Kind, out var adapter) ? adapter.Map(library, version, game)
            : throw new DeploymentException("AdapterNotAvailable", version.Kind.ToString());
}

internal sealed class OrdinaryFiles : IResourceAdapter
{
    public ResourceMap Map(ResourceLibrary library, ResourceVersion version, GameEntry game)
    {
        var source = PathPolicy.Full(version.SourcePath);
        var prefix = PathPolicy.Relative(version.TargetOverride ?? library.TargetRelative);
        var map = new Dictionary<string, ResourceFile>(PathPolicy.Comparer);
        if (File.Exists(source)) map.Add(Path.Combine(prefix, Path.GetFileName(source)), new(source));
        else if (Directory.Exists(source))
            foreach (var file in PathPolicy.Files(source))
            {
                var relative = PathPolicy.Relative(Path.Combine(prefix, Path.GetRelativePath(source, file)));
                if (!map.TryAdd(relative, new(file))) throw new DeploymentException("TargetConflict", relative);
            }
        else throw new DeploymentException("SourceUnavailable", source);
        if (map.Count == 0) throw new DeploymentException("EmptySource", source);
        return new(map, []);
    }
}
