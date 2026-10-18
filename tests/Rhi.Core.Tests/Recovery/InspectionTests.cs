using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Recovery;

public sealed class InspectionTests
{
    private static OperationJournal Pending(Sandbox f)
    {
        File.WriteAllText(f.Target, "original");
        var before = BlobStore.Hash(f.Target)!;
        f.Store.Blobs.Capture(f.Target, before);
        File.WriteAllText(f.Target, "v1");
        var journal = new OperationJournal(Guid.NewGuid(), f.Store.State.Id, f.Game.Id, f.Library.Id,
            OperationKind.Deploy, f.Game.DeploymentRoot, f.Game.ExePath, BlobStore.Hash(f.Game.ExePath)!,
            OperationStatus.Executing, [new("mod.txt", before, BlobStore.Hash(f.Target), DirectoryLease.ExistingParent(f.Target))],
            [], 0, null, DateTimeOffset.UtcNow, DirectoryLease.Inspect(f.Game.DeploymentRoot), FileProtocol: 1);
        f.Store.WriteJournal(journal);
        return journal;
    }

    [Fact]
    public void Preview_uses_recovery_checks_without_mutating_files_or_records()
    {
        using var f = new Sandbox();
        var journal = Pending(f);
        var record = AtomicJson.Fingerprint(f.Store.Pending());
        var preview = f.Engine.InspectRecovery(journal.Id);
        Assert.True(preview.CanRecover);
        Assert.True(Assert.Single(preview.Files).RequiresRestore);
        Assert.False(preview.AlreadyCommitted);
        Assert.Equal("v1", File.ReadAllText(f.Target));
        Assert.Equal(record, AtomicJson.Fingerprint(f.Store.Pending()));
        Assert.Equal(OperationStatus.Restored, f.Engine.Recover(journal.Id).Status);
        Assert.Equal("original", File.ReadAllText(f.Target));
    }

    [Fact]
    public void Unknown_target_change_blocks_preview_and_execution()
    {
        using var f = new Sandbox();
        var journal = Pending(f);
        File.WriteAllText(f.Target, "new external change");
        var preview = f.Engine.InspectRecovery(journal.Id);
        Assert.False(preview.CanRecover);
        Assert.Contains(preview.Issues, i => i.Code == "RecoveryTargetChanged");
        Assert.Equal(OperationStatus.Executing, Assert.Single(f.Store.Pending()).Status);
        Assert.Equal(OperationStatus.NeedsRepair, f.Engine.Recover(journal.Id).Status);
        Assert.Equal("new external change", File.ReadAllText(f.Target));
    }

    [Fact]
    public void Corrupted_backup_blocks_preview_without_changing_pending_record()
    {
        using var f = new Sandbox();
        var journal = Pending(f);
        File.WriteAllText(f.Store.Blobs.PathFor(journal.Files[0].BeforeHash!), "corrupt");
        var preview = f.Engine.InspectRecovery(journal.Id);
        Assert.False(preview.CanRecover);
        Assert.Contains(preview.Issues, i => i.Code == "BackupUnavailable");
        Assert.Equal(OperationStatus.Executing, Assert.Single(f.Store.Pending()).Status);
        Assert.Equal("v1", File.ReadAllText(f.Target));
    }

    [Fact]
    public void Changes_after_preview_are_still_checked_at_execution()
    {
        using var f = new Sandbox();
        var journal = Pending(f);
        Assert.True(f.Engine.InspectRecovery(journal.Id).CanRecover);
        File.WriteAllText(f.Target, "changed after preview");
        Assert.Equal(OperationStatus.NeedsRepair, f.Engine.Recover(journal.Id).Status);
        Assert.Equal("changed after preview", File.ReadAllText(f.Target));
    }

    [Fact]
    public void Already_committed_operation_is_only_finalized_and_never_rolled_back()
    {
        using var f = new Sandbox();
        var journal = Pending(f);
        f.Store.Save(f.Store.State with { CommittedOperations = [journal.Id] });
        File.WriteAllText(f.Target, "user edit after commit");
        var preview = f.Engine.InspectRecovery(journal.Id);
        Assert.True(preview.CanRecover);
        Assert.True(preview.AlreadyCommitted);
        Assert.False(Assert.Single(preview.Files).RequiresRestore);
        Assert.Equal(OperationStatus.Succeeded, f.Engine.Recover(journal.Id).Status);
        Assert.Equal("user edit after commit", File.ReadAllText(f.Target));
    }
}
