using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.Core.Tests.Deployment;

public sealed class DeploymentTests
{
    [Fact]
    public void Deploy_change_clear_preserves_original_and_source()
    {
        using var f = new Sandbox();
        File.WriteAllText(f.Target, "original");
        var first = f.Deploy();
        Assert.Equal(OperationStatus.Succeeded, first.Single().Status);
        Assert.Equal("v1", File.ReadAllText(f.Target));
        var version2 = f.AddVersion("v2");
        f.Run(new(f.Library.Id, version2.Id, OperationKind.Change));
        Assert.Equal("v2", File.ReadAllText(f.Target));
        f.Run(new(f.Library.Id, null, OperationKind.Clear));
        Assert.Equal("original", File.ReadAllText(f.Target));
        Assert.Empty(f.Store.State.Receipts);
        Assert.Equal("v1", File.ReadAllText(f.Source));
    }

    [Fact]
    public void External_change_requires_consent_and_is_archived()
    {
        using var f = new Sandbox();
        f.Deploy();
        File.WriteAllText(f.Target, "manual edit");
        var plan = f.Plan(new DeploymentRequest(f.Library.Id, null, OperationKind.Clear));
        Assert.True(plan.Libraries.Single().RequiresProtection);
        Assert.Throws<DeploymentException>(() => f.Engine.Execute(plan, new(false, false)));
        Assert.Equal("manual edit", File.ReadAllText(f.Target));
        var result = f.Engine.Execute(plan, new(true, true));
        Assert.Equal(OperationStatus.Succeeded, result.Single().Status);
        Assert.False(File.Exists(f.Target));
        Assert.Contains(Directory.GetFiles(Path.Combine(f.Data, "blobs")),
            p => File.ReadAllText(p) == "manual edit");
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(f.Data, "protection"), "*.json"));
    }

    [Fact]
    public void Source_loss_does_not_prevent_clear()
    {
        using var f = new Sandbox();
        f.Deploy();
        File.Move(f.Source, f.Source + ".offline");
        Assert.Equal(OperationStatus.Succeeded,
            f.Run(new(f.Library.Id, null, OperationKind.Clear)).Single().Status);
        Assert.False(File.Exists(f.Target));
    }

    [Fact]
    public void Changed_source_after_confirmation_is_rejected_without_writes()
    {
        using var f = new Sandbox();
        var plan = f.Plan(new DeploymentRequest(f.Library.Id, f.Version.Id, OperationKind.Deploy));
        File.WriteAllText(f.Source, "new source");
        Assert.Throws<DeploymentException>(() => f.Engine.Execute(plan, new(true, true)));
        Assert.False(File.Exists(f.Target));
    }

    [Fact]
    public void Cross_library_target_conflict_blocks_entire_batch()
    {
        using var f = new Sandbox();
        var other = f.Library with { Id = Guid.NewGuid(), Name = "other" };
        f.Store.Save(f.Store.State with { Libraries = [f.Library, other] });
        var plan = f.Plan(new DeploymentRequest(f.Library.Id, f.Version.Id, OperationKind.Deploy),
            new(other.Id, f.Version.Id, OperationKind.Deploy));
        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Issues, i => i.Code == "TargetConflict");
        Assert.False(File.Exists(f.Target));
    }

    [Fact]
    public void Same_content_external_file_still_requires_original_consent()
    {
        using var f = new Sandbox();
        File.WriteAllText(f.Target, "v1");
        var plan = f.Plan(new DeploymentRequest(f.Library.Id, f.Version.Id, OperationKind.Deploy));
        Assert.True(plan.Libraries.Single().RequiresOverwrite);
        Assert.Throws<DeploymentException>(() => f.Engine.Execute(plan, new(false, true)));
        Assert.Empty(f.Store.State.Receipts);
    }

    [Fact]
    public void Cancellation_before_write_leaves_no_receipt()
    {
        using var f = new Sandbox();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var result = f.Engine.Execute(f.Plan(new DeploymentRequest(f.Library.Id, f.Version.Id, OperationKind.Deploy)),
            new(true, true), cancellation: cancel.Token);
        Assert.Equal(OperationStatus.Cancelled, result.Single().Status);
        Assert.False(File.Exists(f.Target));
        Assert.Empty(f.Store.State.Receipts);
    }

    [Fact]
    public void Original_backup_corruption_blocks_clear()
    {
        using var f = new Sandbox();
        File.WriteAllText(f.Target, "original");
        f.Deploy();
        var hash = f.Store.State.Receipts.Single().Files.Single().OriginalHash!;
        File.WriteAllText(f.Store.Blobs.PathFor(hash), "corrupt");
        Assert.False(f.Plan(new DeploymentRequest(f.Library.Id, null, OperationKind.Clear)).CanExecute);
        Assert.Equal("v1", File.ReadAllText(f.Target));
    }
}
