using Rhi.App.Services;
using Rhi.Core.Deployment;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Migration;

namespace Rhi.UiSmoke.Seed;

public static class MigrationFixture
{
    public sealed record Metadata(string Source, string Parent, string Destination, Guid Id,
        string StateHash, MigrationInventory Inventory, bool Interrupted);
    public static void Prepare(string path, bool interrupted)
    {
        var pointer = Bootstrap.Read() ?? throw new InvalidOperationException("CI workspace missing");
        if (!PathPolicy.Within(pointer.Root, Path.Combine(Path.GetTempPath(), "RhiTests"))) throw new InvalidOperationException("Not CI data");
        using var store = WorkspaceStore.Open(pointer.Root, expectedId: pointer.Id);
        store.Save(store.State with { Preferences = new(interrupted ? "Dark" : "Light", interrupted ? "en-US" : "zh-CN") });
        var parent = Path.Combine(Path.GetDirectoryName(pointer.Root)!, "move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var target = WorkspaceMigration.Destination(store, parent);
        var metadata = new Metadata(store.Root, parent, target, store.State.Id, AtomicJson.Fingerprint(store.State), MigrationFiles.Read(store.Root), interrupted);
        AtomicJson.Write(Path.GetFullPath(path), metadata);
        if (!interrupted) return;
        WorkspaceMigration.Prepare(store, target, Bootstrap.Migration);
        try
        {
            WorkspaceMigration.Run(store, Bootstrap.Migration, (root, id) => Bootstrap.Save(new(root, id)),
                checkpoint: phase => { if (phase == "Switched") throw new IOException("CI interrupted migration"); });
        }
        catch (IOException ex) when (ex.Message == "CI interrupted migration") { }
    }
    public static void Verify(string path)
    {
        var fixture = AtomicJson.Read<Metadata>(Path.GetFullPath(path));
        if (Bootstrap.Read() != new DataPointer(fixture.Destination, fixture.Id) || Bootstrap.Migration.Pending() is not null || Directory.Exists(fixture.Source))
            throw new InvalidOperationException("Migration did not complete");
        using var store = WorkspaceStore.Open(fixture.Destination, expectedId: fixture.Id);
        MigrationFiles.Verify(store.Root, fixture.Inventory);
        if (AtomicJson.Fingerprint(store.State) != fixture.StateHash) throw new InvalidOperationException("Migration modified workspace content");
        Console.WriteLine("PASS: whole Data migrated, locator persisted, originals verified, old directory absent.");
    }
}
