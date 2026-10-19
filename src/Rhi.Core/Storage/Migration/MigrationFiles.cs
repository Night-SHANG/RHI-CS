using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Storage.Migration;

public static class MigrationFiles
{
    public static MigrationInventory Read(string root)
    {
        var files = new List<MigrationFile>();
        var directories = new List<string>();
        void Visit(string directory)
        {
            PathPolicy.CheckNoLinks(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(PathPolicy.Comparer))
            {
                PathPolicy.CheckNoLinks(entry);
                var relative = Path.GetRelativePath(root, entry);
                if (Directory.Exists(entry)) { directories.Add(relative); Visit(entry); }
                else if (!PathPolicy.Comparer.Equals(relative, "workspace.lock"))
                    files.Add(new(relative, BlobStore.Hash(entry) ?? throw new DeploymentException("DataChanged", entry)));
            }
        }
        Visit(root);
        return new(files.OrderBy(f => f.Path, PathPolicy.Comparer).ToArray(), directories.Order(PathPolicy.Comparer).ToArray());
    }

    public static void Verify(string root, MigrationInventory expected, bool allowMissing = false)
    {
        var actual = Read(root);
        if (actual.Files.Any(f => !expected.Files.Contains(f)) || actual.Directories.Any(d => !expected.Directories.Contains(d, PathPolicy.Comparer)) ||
            (!allowMissing && (actual.Files.Length != expected.Files.Length || actual.Directories.Length != expected.Directories.Length)))
            throw new DeploymentException("MigrationContentChanged", root);
    }

    public static void CheckPaths(WorkspaceStore source, string destination)
    {
        source.CheckAvailable();
        if (source.Pending().Length > 0) throw new DeploymentException("MigrationNeedsRecovery");
        PathPolicy.Disjoint(source.Root, destination);
        foreach (var game in source.State.Games)
        {
            PathPolicy.DisjointSource(game.InstallRoot, destination);
            PathPolicy.DisjointSource(game.DeploymentRoot, destination);
        }
        foreach (var path in source.State.Libraries.SelectMany(l => l.Versions.Select(v => v.SourcePath)
            .Concat(string.IsNullOrWhiteSpace(l.SourceRoot) ? [] : new[] { l.SourceRoot! })))
            PathPolicy.DisjointSource(path, destination);
    }
}
