using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Storage;

public static class ProtectionCatalog
{
    public static ProtectedOperation? Find(WorkspaceStore store, Guid operationId)
    {
        store.CheckAvailable();
        var root = Path.Combine(store.Root, "protection");
        var path = Path.Combine(root, operationId.ToString("N") + ".json");
        PathPolicy.CheckNoLinks(path);
        if (!File.Exists(path)) return null;
        var entry = AtomicJson.Read<ProtectionEntry>(path);
        if (entry.OperationId != operationId) throw new DeploymentException("InspectionFailed", path);
        return new(entry, ProtectionLayout.FilesDirectory(root, entry));
    }

    public static ProtectionInventory Read(WorkspaceStore store)
    {
        store.CheckAvailable();
        var root = Path.Combine(store.Root, "protection");
        PathPolicy.CheckNoLinks(root);
        long bytes = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            PathPolicy.CheckNoLinks(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                PathPolicy.CheckNoLinks(path);
                if (Directory.Exists(path)) pending.Push(path);
                else bytes = checked(bytes + new FileInfo(path).Length);
            }
        }
        var entries = Directory.EnumerateFiles(root, "*.json").Select(path =>
        {
            PathPolicy.CheckNoLinks(path);
            var entry = AtomicJson.Read<ProtectionEntry>(path);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), entry.OperationId.ToString("N"), StringComparison.OrdinalIgnoreCase))
                throw new DeploymentException("InspectionFailed", path);
            var folder = ProtectionLayout.FilesDirectory(root, entry);
            PathPolicy.CheckNoLinks(folder);
            return new ProtectedOperation(entry, folder);
        }).OrderByDescending(x => x.Entry.CreatedAt).ToArray();
        store.CheckAvailable();
        return new(bytes, DateTimeOffset.UtcNow, entries);
    }
}
