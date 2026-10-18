using Rhi.Core.Deployment;

namespace Rhi.Core.Storage;

public static class StartupWorkspace
{
    public static WorkspaceStore Open(string applicationRoot, string? existingRoot = null, Guid? existingId = null)
    {
        // A disconnected recorded workspace must never silently become a new empty Data folder.
        if (existingRoot is not null) return WorkspaceStore.Open(existingRoot, expectedId: existingId);
        var root = PathPolicy.Target(applicationRoot, "Data");
        var store = WorkspaceStore.Open(root, create: !File.Exists(Path.Combine(root, "workspace.json")));
        try
        {
            foreach (var game in store.State.Games)
            { PathPolicy.DisjointSource(game.InstallRoot, root); PathPolicy.DisjointSource(game.DeploymentRoot, root); }
            foreach (var version in store.State.Libraries.SelectMany(l => l.Versions))
                PathPolicy.DisjointSource(version.SourcePath, root);
            return store;
        }
        catch { store.Dispose(); throw; }
    }
}
