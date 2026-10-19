using Rhi.Core.Deployment;
using Rhi.Core.Storage;
using Rhi.Core.Models;

namespace Rhi.App.Services;

public sealed record DataPointer(string Root, Guid Id);
public static class Bootstrap
{
    public static Rhi.Core.Storage.Migration.MigrationLog Migration => new(Path.Combine(Folder, "data-migration.json"));
    // Only the locator lives here. Catalogs, logs, snapshots and protection stay in the user-selected root.
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI-CS");
    public static DataPointer? Read()
    {
        var path = Path.Combine(Folder, "bootstrap.json");
        PathPolicy.CheckNoLinks(path);
        return File.Exists(path) ? AtomicJson.Read<DataPointer>(path) : null;
    }
    public static void Save(DataPointer pointer)
    {
        PathPolicy.CheckNoLinks(Folder);
        Directory.CreateDirectory(Folder);
        AtomicJson.Write(Path.Combine(Folder, "bootstrap.json"), pointer);
    }

    public static WorkspaceStore StartNew(string root, DataPointer expected)
    {
        if (Read() != expected) throw new DeploymentException("DataLocationChanged");
        // Explicit creation rejects existing contents; it never resets an old ledger.
        var store = WorkspaceStore.Open(root, create: true);
        try
        {
            if (Read() != expected) throw new DeploymentException("DataLocationChanged");
            var history = Path.Combine(Folder, "previous-locations");
            PathPolicy.CheckNoLinks(history);
            Directory.CreateDirectory(history);
            AtomicJson.Write(Path.Combine(history, DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + "_" + Guid.NewGuid().ToString("N") + ".json"), expected);
            Save(new(store.Root, store.State.Id));
            return store;
        }
        catch { store.Dispose(); throw; }
    }
}
