using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Tests.Deployment;

[Collection("Publication flush concurrency")]
public sealed class PublicationBatchIntegrationTests
{
    [Fact]
    public void Sixty_five_publications_are_bounded_and_all_barriers_finish_before_commit()
    {
        using var box = new Sandbox();
        var plan = FolderPlan(box, 65);
        var queued = 0;
        var maximum = 0;
        var flushed = 0;
        var completed = 0;
        var committed = false;
        var engine = new DeploymentEngine(box.Store, (phase, index) =>
        {
            if (phase == "PublishPendingFlush")
            {
                maximum = Math.Max(maximum, ++queued);
                Assert.InRange(queued, 1, 32);
                var target = Path.Combine(box.Game.DeploymentRoot, plan.Libraries[0].Changes[index].RelativePath);
                Assert.Throws<IOException>(() => File.Open(target, FileMode.Open, FileAccess.Write).Dispose());
            }
            if (phase == "PublishFlushed") Interlocked.Increment(ref flushed);
            if (phase == "AfterWrite")
            {
                queued = 0;
                completed++;
                var target = Path.Combine(box.Game.DeploymentRoot, plan.Libraries[0].Changes[index].RelativePath);
                using var writable = File.Open(target, FileMode.Open, FileAccess.Write);
            }
            if (phase == "BeforeWrite" && index == 32) Assert.Equal(32, completed);
            if (phase != "BeforeCommit") return;
            Assert.Equal(65, flushed);
            Assert.Equal(65, completed);
            Assert.Equal(0, queued);
            committed = true;
        });
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(engine.Execute(plan, new(true, true))).Status);
        Assert.True(committed);
        Assert.Equal(32, maximum);
        Assert.Equal(65, Assert.Single(box.Store.State.Receipts).Files.Length);
        Assert.Empty(box.Store.Pending());
        Assert.Empty(Directory.GetFiles(box.Game.DeploymentRoot, ".rhi-*"));
    }

    [Fact]
    public void First_afterwrite_failure_restores_later_files_already_published_in_the_batch()
    {
        using var box = new Sandbox();
        var plan = FolderPlan(box, 5, originals: true);
        var published = 0;
        var observed = false;
        var engine = new DeploymentEngine(box.Store, (phase, index) =>
        {
            if (phase == "PublishPendingFlush") published++;
            if (phase != "AfterWrite" || index != 0) return;
            Assert.Equal(5, published);
            observed = true;
            throw new IOException("first completion fixture");
        });
        Assert.Equal(OperationStatus.Restored, Assert.Single(engine.Execute(plan, new(true, true))).Status);
        Assert.True(observed);
        AssertOriginals(box, plan);
    }

    [Fact]
    public void Queued_publication_failure_releases_owned_handles_before_rollback()
    {
        using var box = new Sandbox();
        var plan = FolderPlan(box, 4, originals: true);
        var observed = false;
        var engine = new DeploymentEngine(box.Store, (phase, index) =>
        {
            if (phase != "PublishPendingFlush" || index != 2) return;
            observed = true;
            throw new IOException("ownership transfer fixture");
        });
        Assert.Equal(OperationStatus.Restored, Assert.Single(engine.Execute(plan, new(true, true))).Status);
        Assert.True(observed);
        AssertOriginals(box, plan);
    }

    [Fact]
    public async Task Cancellation_during_flush_waits_for_all_barriers_then_restores_originals()
    {
        using var box = new Sandbox();
        var plan = FolderPlan(box, 4, originals: true);
        using var cancellation = new CancellationTokenSource();
        using var entered = new CountdownEvent(4);
        using var release = new ManualResetEventSlim();
        var flushed = 0;
        var committed = false;
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase == "PublishFlushing")
            {
                entered.Signal();
                Assert.True(release.Wait(TimeSpan.FromSeconds(20)));
            }
            if (phase == "PublishFlushed") Interlocked.Increment(ref flushed);
            if (phase == "BeforeCommit") committed = true;
        });
        var execution = Task.Run(() => engine.Execute(plan, new(true, true), cancellation: cancellation.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(20)));
            cancellation.Cancel();
            Assert.False(execution.IsCompleted);
            Assert.Empty(box.Store.State.Receipts);
            Assert.Empty(box.Store.State.CommittedOperations);
        }
        finally { release.Set(); await execution; }
        var result = Assert.Single(await execution);
        Assert.Equal(OperationStatus.Restored, result.Status);
        Assert.Equal("StoppedRestored", result.Detail);
        Assert.Equal(4, flushed);
        Assert.False(committed);
        AssertOriginals(box, plan);
    }

    [Fact]
    public async Task Flush_failure_with_outstanding_work_never_commits_and_preserves_all_originals()
    {
        using var box = new Sandbox();
        var plan = FolderPlan(box, 4, originals: true);
        using var entered = new CountdownEvent(4);
        using var failed = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var flushed = 0;
        var committed = false;
        var engine = new DeploymentEngine(box.Store, (phase, index) =>
        {
            if (phase == "PublishFlushing")
            {
                entered.Signal();
                Assert.True(entered.Wait(TimeSpan.FromSeconds(20)));
                if (index == 0) { failed.Set(); throw new IOException("failed flush fixture"); }
                Assert.True(release.Wait(TimeSpan.FromSeconds(20)));
            }
            if (phase == "PublishFlushed") Interlocked.Increment(ref flushed);
            if (phase == "BeforeCommit") committed = true;
        });
        var execution = Task.Run(() => engine.Execute(plan, new(true, true)));
        try
        {
            Assert.True(failed.Wait(TimeSpan.FromSeconds(20)));
            Assert.False(execution.IsCompleted);
            Assert.Empty(box.Store.State.Receipts);
            Assert.Empty(box.Store.State.CommittedOperations);
        }
        finally { release.Set(); await execution; }
        Assert.Equal(OperationStatus.Restored, Assert.Single(await execution).Status);
        Assert.Equal(3, flushed);
        Assert.False(committed);
        AssertOriginals(box, plan);
    }

    [Fact]
    public void Cancellation_with_a_partly_queued_batch_releases_it_before_rollback()
    {
        using var box = new Sandbox();
        var plan = FolderPlan(box, 4, originals: true);
        using var cancellation = new CancellationTokenSource();
        var flushed = 0;
        var observed = false;
        var engine = new DeploymentEngine(box.Store, (phase, index) =>
        {
            if (phase == "PublishFlushing") Interlocked.Increment(ref flushed);
            if (phase != "PublishPendingFlush" || index != 1) return;
            observed = true;
            cancellation.Cancel();
        });
        var result = Assert.Single(engine.Execute(plan, new(true, true), cancellation: cancellation.Token));
        Assert.True(observed);
        Assert.Equal(OperationStatus.Restored, result.Status);
        Assert.Equal("StoppedRestored", result.Detail);
        Assert.Equal(0, flushed);
        AssertOriginals(box, plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Clear_crosses_batch_boundaries_with_only_required_restoration_barriers(bool originals)
    {
        using var box = new Sandbox();
        var initial = FolderPlan(box, 33, originals);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(box.Engine.Execute(initial, new(true, true))).Status);
        var flushed = 0;
        var completed = 0;
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase == "PublishFlushed") Interlocked.Increment(ref flushed);
            if (phase == "AfterWrite") completed++;
        });
        var clear = box.Plan(new DeploymentRequest(box.Library.Id, null, OperationKind.Clear));
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(engine.Execute(clear, new(true, true))).Status);
        Assert.Equal(originals ? 33 : 0, flushed);
        Assert.Equal(33, completed);
        foreach (var change in initial.Libraries[0].Changes)
        {
            var path = Path.Combine(box.Game.DeploymentRoot, change.RelativePath);
            if (originals) Assert.Equal("original-" + Path.GetFileName(path), File.ReadAllText(path));
            else Assert.False(File.Exists(path));
        }
        Assert.Empty(box.Store.State.Receipts);
        Assert.Empty(box.Store.Pending());
        Assert.Empty(Directory.GetFiles(box.Game.DeploymentRoot, ".rhi-*"));
    }

    [Fact]
    public void Mixed_noop_removal_and_replacement_batch_flushes_only_new_publications()
    {
        using var box = new Sandbox();
        var initial = FolderPlan(box, 3);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(box.Engine.Execute(initial, new(true, true))).Status);
        var source = Path.Combine(box.Root, "next-version");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "mod.txt"), "v1"); // Existing bytes: no-op.
        File.WriteAllText(Path.Combine(source, "file-001.txt"), "replacement");
        File.WriteAllText(Path.Combine(source, "new.txt"), "added"); // file-002.txt is removed.
        var version = box.Version with { Id = Guid.NewGuid(), SourcePath = source };
        var library = box.Store.State.Libraries.Single();
        box.Store.Save(box.Store.State with { Libraries = [library with { Versions = [.. library.Versions, version] }] });
        var flushed = 0;
        var completed = 0;
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase == "PublishFlushed") Interlocked.Increment(ref flushed);
            if (phase == "AfterWrite") completed++;
        });
        var plan = box.Plan(new DeploymentRequest(box.Library.Id, version.Id, OperationKind.Change));
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(engine.Execute(plan, new(true, true))).Status);
        Assert.Equal(2, flushed);
        Assert.Equal(4, completed);
        Assert.Equal("v1", File.ReadAllText(box.Target));
        Assert.Equal("replacement", File.ReadAllText(Path.Combine(box.Game.DeploymentRoot, "file-001.txt")));
        Assert.Equal("added", File.ReadAllText(Path.Combine(box.Game.DeploymentRoot, "new.txt")));
        Assert.False(File.Exists(Path.Combine(box.Game.DeploymentRoot, "file-002.txt")));
        Assert.Equal(3, Assert.Single(box.Store.State.Receipts).Files.Length);
        Assert.Empty(box.Store.Pending());
        Assert.Empty(Directory.GetFiles(box.Game.DeploymentRoot, ".rhi-*"));
    }

    private static BatchPlan FolderPlan(Sandbox box, int count, bool originals = false)
    {
        var source = Path.GetDirectoryName(box.Source)!;
        for (var i = 1; i < count; i++) File.WriteAllText(Path.Combine(source, $"file-{i:D3}.txt"), $"new-{i}");
        if (originals)
            foreach (var file in Directory.GetFiles(source))
                File.WriteAllText(Path.Combine(box.Game.DeploymentRoot, Path.GetFileName(file)), "original-" + Path.GetFileName(file));
        box.Store.Save(box.Store.State with
        {
            Libraries = [box.Library with { Versions = [box.Version with { SourcePath = source }] }]
        });
        return box.Plan(new DeploymentRequest(box.Library.Id, box.Version.Id, OperationKind.Deploy));
    }

    private static void AssertOriginals(Sandbox box, BatchPlan plan)
    {
        foreach (var change in plan.Libraries[0].Changes)
        {
            var path = Path.Combine(box.Game.DeploymentRoot, change.RelativePath);
            Assert.Equal("original-" + Path.GetFileName(path), File.ReadAllText(path));
            using var released = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        Assert.Empty(box.Store.State.Receipts);
        Assert.Empty(box.Store.State.CommittedOperations);
        Assert.Empty(box.Store.Pending());
        Assert.Empty(Directory.GetFiles(box.Game.DeploymentRoot, ".rhi-*"));
    }
}
