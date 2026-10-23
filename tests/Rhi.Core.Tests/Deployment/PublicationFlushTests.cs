using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Tests.Deployment;

public sealed class PublicationFlushTests
{
    [Fact]
    public void Publication_flush_error_does_not_commit_or_discard_the_original()
    {
        using var box = new Sandbox();
        File.WriteAllText(box.Target, "original");
        var observed = false;
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase != "PublishFlushing") return;
            observed = true;
            throw new IOException("simulated publication flush failure");
        });
        var result = Assert.Single(engine.Execute(
            box.Plan(new DeploymentRequest(box.Library.Id, box.Version.Id, OperationKind.Deploy)), new(true, true)));
        Assert.True(observed);
        Assert.Equal(OperationStatus.Restored, result.Status);
        Assert.Equal("original", File.ReadAllText(box.Target));
        Assert.Empty(box.Store.State.Receipts);
        Assert.Empty(box.Store.State.CommittedOperations);
        Assert.Empty(Directory.GetFiles(box.Game.DeploymentRoot, ".rhi-*"));
    }
    [Fact]
    public void Incomplete_uncommitted_publication_preserves_unknown_bytes_and_parked_original()
    {
        // Construct the recovery state, not a claim to simulate physical power loss.
        using var box = new Sandbox();
        File.WriteAllText(box.Target, "original");
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase != "AfterWrite") return;
            File.WriteAllText(box.Target, "v");
            throw new IOException("incomplete published-file recovery fixture");
        });
        var result = Assert.Single(engine.Execute(
            box.Plan(new DeploymentRequest(box.Library.Id, box.Version.Id, OperationKind.Deploy)), new(true, true)));
        Assert.Equal(OperationStatus.NeedsRepair, result.Status);
        Assert.Equal("v", File.ReadAllText(box.Target));
        var original = Assert.Single(Directory.GetFiles(box.Game.DeploymentRoot, "*.before"));
        Assert.Equal("original", File.ReadAllText(original));
        Assert.Empty(box.Store.State.Receipts);
        var journal = Assert.Single(box.Store.Pending());
        Assert.Equal(OperationStatus.NeedsRepair, new DeploymentEngine(box.Store).Recover(journal.Id).Status);
        Assert.Equal("v", File.ReadAllText(box.Target));
        Assert.Equal("original", File.ReadAllText(original));
    }

}
