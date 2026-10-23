using System.Text.Json.Nodes;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Storage;

public sealed class ProtectionRecordTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_one_record_keeps_all_files_and_other_records_across_refresh_and_restart(bool legacy)
    {
        using var f = new Sandbox();
        var removed = AddCopy(f, legacy);
        var retained = AddCopy(f, false);
        var before = Snapshot(f.Root);
        var directories = Directory.GetDirectories(f.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var bytes = ProtectionCatalog.Read(f.Store).Bytes;
        var removedCopy = ProtectionCatalog.Find(f.Store, removed)!;

        Assert.True(ProtectionCatalog.RemoveRecord(f.Store, removed, confirmed: true));
        Assert.Equal(retained, Assert.Single(ProtectionCatalog.Read(f.Store).Operations).Entry.OperationId);
        Assert.Equal(bytes, ProtectionCatalog.Read(f.Store).Bytes);
        Assert.Equal(removedCopy.Directory, ProtectionCatalog.Find(f.Store, removed)!.Directory);
        AssertSnapshot(before, Snapshot(f.Root));
        Assert.Equal(directories, Directory.GetDirectories(f.Root, "*", SearchOption.AllDirectories).Order().ToArray());

        // Saving the 1530417 general settings must not revive hidden records.
        f.Store.Save(f.Store.State with { Preferences = f.Store.State.Preferences with
            { Theme = "Dark", Language = "en-US", LibraryRoot = Path.Combine(f.Root, "libraries") } });
        f.Store.Dispose();
        using var reopened = WorkspaceStore.Open(f.Data);
        Assert.Equal(removed, Assert.Single(reopened.State.HiddenProtectionIds));
        Assert.Equal(retained, Assert.Single(ProtectionCatalog.Read(reopened).Operations).Entry.OperationId);
        Assert.Equal(bytes, ProtectionCatalog.Read(reopened).Bytes);
        Assert.Equal(removedCopy.Directory, ProtectionCatalog.Find(reopened, removed)!.Directory);
        AssertSnapshot(before, Snapshot(f.Root));
    }

    [Fact]
    public void Cancel_unknown_and_repeated_removal_do_not_write_anything()
    {
        using var f = new Sandbox();
        var id = AddCopy(f, false);
        var before = Snapshot(f.Root, includeWorkspace: true);
        Assert.False(ProtectionCatalog.RemoveRecord(f.Store, id, confirmed: false));
        Assert.False(ProtectionCatalog.RemoveRecord(f.Store, Guid.NewGuid(), confirmed: true));
        Assert.Equal(id, Assert.Single(ProtectionCatalog.Read(f.Store).Operations).Entry.OperationId);
        Assert.Empty(f.Store.State.HiddenProtectionIds);
        AssertSnapshot(before, Snapshot(f.Root, includeWorkspace: true));
        Assert.True(ProtectionCatalog.RemoveRecord(f.Store, id, confirmed: true));
        before = Snapshot(f.Root, includeWorkspace: true);
        Assert.False(ProtectionCatalog.RemoveRecord(f.Store, id, confirmed: true));
        AssertSnapshot(before, Snapshot(f.Root, includeWorkspace: true));
    }

    [Fact]
    public void Old_workspace_without_hidden_ids_loads_without_rewriting_and_keeps_all_records_visible()
    {
        using var f = new Sandbox();
        var id = AddCopy(f, true);
        f.Store.Dispose();
        var path = Path.Combine(f.Data, "workspace.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(json.Remove("HiddenProtectionIds"));
        File.WriteAllText(path, json.ToJsonString());
        var before = File.ReadAllBytes(path);
        using var reopened = WorkspaceStore.Open(f.Data);
        Assert.Empty(reopened.State.HiddenProtectionIds);
        Assert.Equal(id, Assert.Single(ProtectionCatalog.Read(reopened).Operations).Entry.OperationId);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private static Guid AddCopy(Sandbox f, bool legacy)
    {
        var id = Guid.NewGuid();
        var hash = BlobStore.Hash(f.Source)!;
        f.Store.Blobs.Capture(f.Source, hash);
        if (!legacy)
        {
            f.Store.Protect(new(id, f.Game.Id, f.Library.Id, f.Game.DeploymentRoot,
                [new("mod.txt", hash, null)], DateTimeOffset.UtcNow, "game", "library",
                VersionLabel: "v1", FullVersion: true));
            return id;
        }
        var root = Path.Combine(f.Data, "protection");
        var folder = Path.Combine(root, id.ToString("N"), "files");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "legacy.ini"), "old protected data");
        AtomicJson.Write(Path.Combine(root, id.ToString("N") + ".json"), new
        {
            OperationId = id, GameId = f.Game.Id, LibraryId = f.Library.Id,
            TargetRoot = f.Game.DeploymentRoot, Files = Array.Empty<JournalFile>(), CreatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static Dictionary<string, byte[]> Snapshot(string root, bool includeWorkspace = false) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "workspace.lock" &&
                (includeWorkspace || Path.GetFileName(path) != "workspace.json"))
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);

    private static void AssertSnapshot(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
    }
}
