using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Tests.Deployment;

public sealed class FailureTests
{
    [Fact]
    public void Failure_after_write_restores_actual_preoperation_state()
    {
        using var f = new Sandbox((phase, _) => { if (phase == "AfterWrite") throw new IOException("injected"); });
        File.WriteAllText(f.Target, "original");
        var result = f.Deploy().Single();
        Assert.Equal(OperationStatus.Restored, result.Status);
        Assert.Equal("original", File.ReadAllText(f.Target));
        Assert.Empty(f.Store.State.Receipts);
        Assert.Empty(f.Store.Pending());
    }

    [Fact]
    public void Failure_before_commit_restores_files_and_keeps_old_receipt()
    {
        using var f = new Sandbox();
        f.Deploy();
        File.WriteAllText(f.Target, "user edit");
        var version = f.AddVersion("v2");
        var engine = new DeploymentEngine(f.Store, (phase, _) => { if (phase == "BeforeCommit") throw new IOException("disk"); });
        var result = engine.Execute(f.Plan(new(f.Library.Id, version.Id, OperationKind.Change)), new(true, true));
        Assert.Equal(OperationStatus.Restored, result.Single().Status);
        Assert.Equal("user edit", File.ReadAllText(f.Target));
        Assert.Equal(f.Version.Id, f.Store.State.Receipts.Single().VersionId);
    }

    [Fact]
    public void Unknown_change_during_recovery_is_not_overwritten()
    {
        using var f = new Sandbox();
        var engine = new DeploymentEngine(f.Store, (phase, _) =>
        {
            if (phase != "AfterWrite") return;
            File.WriteAllText(f.Target, "another program");
            throw new IOException("injected");
        });
        var result = engine.Execute(f.Plan(new(f.Library.Id, f.Version.Id, OperationKind.Deploy)), new(true, true));
        Assert.Equal(OperationStatus.NeedsRepair, result.Single().Status);
        Assert.Equal("another program", File.ReadAllText(f.Target));
        Assert.Single(f.Store.Pending());
        Assert.False(f.Plan(new(f.Library.Id, f.Version.Id, OperationKind.Deploy)).CanExecute);
    }

    [Fact]
    public void Interrupted_journal_can_be_recovered_without_source()
    {
        using var f = new Sandbox();
        File.WriteAllText(f.Target, "original");
        var before = BlobStore.Hash(f.Target)!;
        f.Store.Blobs.Capture(f.Target, before);
        File.WriteAllText(f.Target, "v1");
        var journal = new OperationJournal(Guid.NewGuid(), f.Store.State.Id, f.Game.Id, f.Library.Id,
            OperationKind.Deploy, f.Game.DeploymentRoot, f.Game.ExePath, BlobStore.Hash(f.Game.ExePath)!,
            OperationStatus.Executing, [new("mod.txt", before, BlobStore.Hash(f.Target), DirectoryLease.ExistingParent(f.Target))],
            [], 0, null, DateTimeOffset.UtcNow, DirectoryLease.Inspect(f.Game.DeploymentRoot), FileProtocol: 1);
        f.Store.WriteJournal(journal);
        File.Move(f.Source, f.Source + ".offline");
        Assert.Equal(OperationStatus.Restored, f.Engine.Recover(journal.Id).Status);
        Assert.Equal("original", File.ReadAllText(f.Target));
    }

    [Fact]
    public void Batch_keeps_success_restores_failed_and_stops_remaining()
    {
        var writeCount = 0;
        using var f = new Sandbox((phase, _) =>
        { if (phase == "AfterWrite" && ++writeCount == 2) throw new IOException("second library"); });
        var second = f.Library with { Id = Guid.NewGuid(), Name = "B", TargetRelative = "B" };
        var third = f.Library with { Id = Guid.NewGuid(), Name = "C", TargetRelative = "C" };
        f.Store.Save(f.Store.State with { Libraries = [f.Library, second, third] });
        var plan = f.Plan(new(f.Library.Id, f.Version.Id, OperationKind.Deploy),
            new(second.Id, f.Version.Id, OperationKind.Deploy), new(third.Id, f.Version.Id, OperationKind.Deploy));
        var result = f.Engine.Execute(plan, new(true, true));
        Assert.Equal(new[] { OperationStatus.Succeeded, OperationStatus.Restored, OperationStatus.NotExecuted },
            result.Select(r => r.Status));
        Assert.Equal("v1", File.ReadAllText(f.Target));
        Assert.False(File.Exists(Path.Combine(f.Game.DeploymentRoot, "B", "mod.txt")));
        Assert.False(File.Exists(Path.Combine(f.Game.DeploymentRoot, "C", "mod.txt")));
        Assert.Single(f.Store.State.Receipts);
    }

    [Fact]
    public void Lost_data_root_does_not_create_an_empty_ledger()
    {
        using var f = new Sandbox();
        var ledger = Path.Combine(f.Data, "workspace.json");
        File.Move(ledger, ledger + ".offline");
        Assert.Throws<DeploymentException>(() => f.Store.Save(f.Store.State));
        Assert.False(File.Exists(ledger));
    }
}
