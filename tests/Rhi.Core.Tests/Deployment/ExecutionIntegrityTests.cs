using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Tests.Deployment;

public sealed class ExecutionIntegrityTests
{
    [Theory]
    [InlineData("clear")]
    [InlineData("overwrite")]
    [InlineData("noop")]
    public void Target_edited_after_batch_validation_is_never_discarded(string operation)
    {
        using var box = new Sandbox();
        box.Deploy();
        var version = operation == "overwrite" ? box.AddVersion("v2") : box.Version;
        var request = new DeploymentRequest(box.Library.Id, operation == "clear" ? null : version.Id,
            operation == "clear" ? OperationKind.Clear : OperationKind.Change);
        var plan = box.Plan(request);
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase != "BeforeWrite") return;
            var stamp = File.GetLastWriteTimeUtc(box.Target);
            File.WriteAllText(box.Target, "zz");
            File.SetLastWriteTimeUtc(box.Target, stamp);
        });
        var result = Assert.Single(engine.Execute(plan, new(true, true)));
        Assert.NotEqual(OperationStatus.Succeeded, result.Status);
        Assert.Equal("zz", File.ReadAllText(box.Target));
        Assert.Equal(box.Version.Id, Assert.Single(box.Store.State.Receipts).VersionId);
    }

    [Fact]
    public void Source_changed_after_batch_validation_is_not_published_or_receipted()
    {
        using var box = new Sandbox();
        var plan = box.Plan(new DeploymentRequest(box.Library.Id, box.Version.Id, OperationKind.Deploy));
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase == "BeforeWrite") File.WriteAllText(box.Source, "changed after preflight");
        });
        Assert.NotEqual(OperationStatus.Succeeded, Assert.Single(engine.Execute(plan, new(true, true))).Status);
        Assert.False(File.Exists(box.Target));
        Assert.Empty(box.Store.State.Receipts);
        Assert.Empty(Directory.GetFiles(box.Game.DeploymentRoot, ".rhi-*"));
        Assert.Equal("changed after preflight", File.ReadAllText(box.Source));
    }

    [Fact]
    public void Copied_staging_bytes_remain_locked_until_publication()
    {
        using var box = new Sandbox();
        var checkedStage = false;
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase != "CopyCompleted") return;
            checkedStage = true;
            var stage = Assert.Single(Directory.GetFiles(box.Game.DeploymentRoot, "*.next"));
            Assert.Throws<IOException>(() => File.WriteAllText(stage, "changed staging"));
        });
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(engine.Execute(
            box.Plan(new DeploymentRequest(box.Library.Id, box.Version.Id, OperationKind.Deploy)), new(true, true))).Status);
        Assert.True(checkedStage);
        Assert.Equal("v1", File.ReadAllText(box.Target));
    }

    [Fact]
    public void Parked_original_is_locked_until_commit_and_cleanup_uses_its_handle()
    {
        using var box = new Sandbox();
        box.Deploy();
        var checkedOriginal = false;
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase != "BeforeCommit") return;
            checkedOriginal = true;
            var original = Assert.Single(Directory.GetFiles(box.Game.DeploymentRoot, "*.before"));
            Assert.Throws<IOException>(() => File.WriteAllText(original, "new data"));
            Assert.Throws<IOException>(() => File.Delete(original));
        });
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(engine.Execute(
            box.Plan(new DeploymentRequest(box.Library.Id, null, OperationKind.Clear)), new(true, true))).Status);
        Assert.True(checkedOriginal);
        Assert.Empty(Directory.GetFiles(box.Game.DeploymentRoot, ".rhi-*"));
    }
    [Fact]
    public void Partial_copy_locks_source_and_releases_owned_handles_on_failure()
    {
        using var box = new Sandbox();
        File.WriteAllText(box.Source, new string('s', 400000));
        File.WriteAllText(box.Target, "original");
        var observed = false;
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase != "StageWriting") return;
            Assert.Throws<IOException>(() => File.WriteAllText(box.Source, "changed"));
            Assert.Throws<IOException>(() => File.Delete(box.Source));
            observed = true; // Assert outside Execute that both checks completed, not swallowed.
            throw new IOException("partial copy failure fixture");
        });
        Assert.Equal(OperationStatus.Restored, Assert.Single(engine.Execute(
            box.Plan(new DeploymentRequest(box.Library.Id, box.Version.Id, OperationKind.Deploy)), new(true, true))).Status);
        Assert.True(observed);
        Assert.Equal("original", File.ReadAllText(box.Target));
        Assert.Empty(Directory.GetFiles(box.Game.DeploymentRoot, ".rhi-*"));
        Assert.Equal(new string('s', 400000), File.ReadAllText(box.Source));
        File.AppendAllText(box.Source, "released");
        File.AppendAllText(box.Target, "released");
    }

}
