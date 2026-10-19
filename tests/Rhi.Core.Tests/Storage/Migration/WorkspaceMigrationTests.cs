using System.Text;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Migration;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Storage.Migration;

public sealed class WorkspaceMigrationTests
{
    [Fact]
    public void Whole_workspace_moves_and_original_restore_still_works()
    {
        using var f = new Sandbox();
        File.WriteAllText(f.Target, "original", Encoding.UTF8);
        Assert.Equal(OperationStatus.Succeeded, f.Deploy()[0].Status);
        Directory.CreateDirectory(Path.Combine(f.Data, "extra", "empty"));
        File.WriteAllText(Path.Combine(f.Data, "extra", "notes.txt"), "unindexed data", Encoding.UTF8);
        File.SetAttributes(Path.Combine(f.Data, "extra", "notes.txt"), FileAttributes.Hidden);
        var state = AtomicJson.Fingerprint(f.Store.State);
        var source = BlobStore.Hash(f.Source);
        var before = MigrationFiles.Read(f.Data);
        var target = Path.Combine(f.Root, "moved");
        var log = new MigrationLog(Path.Combine(f.Root, "migration.json"));
        WorkspaceMigration.Prepare(f.Store, target, log);
        var pointer = f.Data;
        WorkspaceMigration.Run(f.Store, log, (root, _) => pointer = root);
        Assert.Equal(target, pointer);
        Assert.False(Directory.Exists(f.Data));
        Assert.Null(log.Pending());
        MigrationFiles.Verify(target, before);
        using var moved = WorkspaceStore.Open(target);
        Assert.Equal(state, AtomicJson.Fingerprint(moved.State));
        Assert.Equal(source, BlobStore.Hash(f.Source));
        Assert.True((File.GetAttributes(Path.Combine(target, "extra", "notes.txt")) & FileAttributes.Hidden) != 0);
        var plan = new DeploymentPlanner(moved).Build(moved.State, f.Game.Id, [new(f.Library.Id, null, OperationKind.Clear)]);
        Assert.Equal(OperationStatus.Succeeded, new DeploymentEngine(moved).Execute(plan, new(true, true))[0].Status);
        Assert.Equal("original", File.ReadAllText(f.Target, Encoding.UTF8));
    }

    [Theory]
    [InlineData("Copied")]
    [InlineData("Verified")]
    [InlineData("Switched")]
    [InlineData("FileRemoved")]
    [InlineData("Cleaned")]
    public void Restart_resumes_from_each_durable_boundary(string interruption)
    {
        using var f = new Sandbox();
        File.WriteAllText(Path.Combine(f.Data, "retained.txt"), "whole Data", Encoding.UTF8);
        var target = Path.Combine(f.Root, "moved");
        var logPath = Path.Combine(f.Root, "migration.json");
        var pointerPath = Path.Combine(f.Root, "pointer.json");
        AtomicJson.Write(pointerPath, f.Data);
        var log = new MigrationLog(logPath);
        var record = WorkspaceMigration.Prepare(f.Store, target, log);
        Assert.Throws<IOException>(() => WorkspaceMigration.Run(f.Store, log, (root, _) => AtomicJson.Write(pointerPath, root),
            checkpoint: phase => { if (phase == interruption) throw new IOException("simulated interruption"); }));
        f.Store.Dispose();
        Assert.NotNull(log.Pending());
        var authoritative = AtomicJson.Read<string>(pointerPath);
        using (var reopened = WorkspaceStore.Open(authoritative, expectedId: record.WorkspaceId))
            WorkspaceMigration.Run(reopened, new(logPath), (root, _) => AtomicJson.Write(pointerPath, root));
        Assert.False(Directory.Exists(f.Data));
        Assert.Equal(target, AtomicJson.Read<string>(pointerPath));
        Assert.Null(new MigrationLog(logPath).Pending());
        MigrationFiles.Verify(target, record.Inventory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Changed_destination_or_new_source_file_prevents_cleanup(bool changeDestination)
    {
        using var f = new Sandbox();
        File.WriteAllText(Path.Combine(f.Data, "notes.txt"), "original", Encoding.UTF8);
        var target = Path.Combine(f.Root, "moved");
        var log = new MigrationLog(Path.Combine(f.Root, "migration.json"));
        WorkspaceMigration.Prepare(f.Store, target, log);
        Assert.Throws<IOException>(() => WorkspaceMigration.Run(f.Store, log, (_, _) => { },
            checkpoint: phase => { if (phase == "Switched") throw new IOException("interrupted"); }));
        f.Store.Dispose();
        var changed = changeDestination ? Path.Combine(target, "notes.txt") : Path.Combine(f.Data, "new-user-file.txt");
        File.WriteAllText(changed, "keep me", Encoding.UTF8);
        using var reopened = WorkspaceStore.Open(target);
        Assert.Throws<DeploymentException>(() => WorkspaceMigration.Run(reopened, log, (_, _) => { }));
        Assert.Equal("keep me", File.ReadAllText(changed, Encoding.UTF8));
        Assert.True(File.Exists(Path.Combine(f.Data, "notes.txt")));
        Assert.NotNull(log.Pending());
    }

    [Fact]
    public void Destination_conflicts_and_game_or_source_overlap_are_rejected_before_journal()
    {
        using var f = new Sandbox();
        var log = new MigrationLog(Path.Combine(f.Root, "migration.json"));
        foreach (var target in new[] { f.Data, Path.Combine(f.Data, "nested"), f.Root,
            Path.Combine(f.Game.InstallRoot, "Data"), Path.Combine(Path.GetDirectoryName(f.Source)!, "Data") })
            Assert.ThrowsAny<Exception>(() => WorkspaceMigration.Prepare(f.Store, target, log));
        var occupied = Path.Combine(f.Root, "occupied");
        Directory.CreateDirectory(occupied);
        Assert.Equal("MigrationTargetExists", Assert.Throws<DeploymentException>(() => WorkspaceMigration.Prepare(f.Store, occupied, log)).Code);
        Assert.Null(log.Read());
        f.Store.CheckAvailable();
    }

    [Fact]
    public void Pending_recovery_blocks_migration()
    {
        using var f = new Sandbox();
        f.Store.WriteJournal(new(Guid.NewGuid(), f.Store.State.Id, f.Game.Id, f.Library.Id, OperationKind.Deploy,
            f.Game.DeploymentRoot, f.Game.ExePath, BlobStore.Hash(f.Game.ExePath)!, OperationStatus.NeedsRepair,
            [], [], -1, "interrupted", DateTimeOffset.Now));
        var log = new MigrationLog(Path.Combine(f.Root, "migration.json"));
        Assert.Equal("MigrationNeedsRecovery", Assert.Throws<DeploymentException>(() => WorkspaceMigration.Prepare(f.Store, Path.Combine(f.Root, "moved"), log)).Code);
        Assert.Null(log.Read());
    }

    [Fact]
    public void Locked_source_keeps_locator_and_can_be_retried()
    {
        using var f = new Sandbox();
        var file = Path.Combine(f.Data, "notes.txt");
        File.WriteAllText(file, "locked", Encoding.UTF8);
        var log = new MigrationLog(Path.Combine(f.Root, "migration.json"));
        var target = Path.Combine(f.Root, "moved");
        WorkspaceMigration.Prepare(f.Store, target, log);
        var switched = false;
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => WorkspaceMigration.Run(f.Store, log, (_, _) => switched = true));
        Assert.False(switched);
        f.Store.CheckAvailable();
        WorkspaceMigration.Run(f.Store, log, (_, _) => switched = true);
        Assert.True(switched);
        Assert.False(Directory.Exists(f.Data));
    }

    [Fact]
    public void Real_cross_volume_copy_preserves_data()
    {
        using var f = new Sandbox();
        // This project runs tests only on the Windows GitHub runner, which supplies a separate D: volume.
        var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed &&
            !PathPolicy.Comparer.Equals(d.RootDirectory.FullName, Path.GetPathRoot(f.Root)));
        Assert.NotNull(drive);
        var parent = Path.Combine(drive.RootDirectory.FullName, "RhiMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var target = WorkspaceMigration.Destination(f.Store, parent);
        var log = new MigrationLog(Path.Combine(f.Root, "migration.json"));
        var inventory = WorkspaceMigration.Prepare(f.Store, target, log).Inventory;
        WorkspaceMigration.Run(f.Store, log, (_, _) => { });
        Assert.False(Directory.Exists(f.Data));
        MigrationFiles.Verify(target, inventory);
    }
}
