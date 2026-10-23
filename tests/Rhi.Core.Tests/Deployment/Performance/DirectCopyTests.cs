using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.Core.Tests.Deployment;

public sealed class DirectCopyTests
{
    [Fact]
    public void Sources_are_not_cached_and_the_journal_is_not_rewritten_during_copy()
    {
        using var box = new Sandbox();
        var folder = Path.GetDirectoryName(box.Source)!;
        for (var i = 0; i < 32; i++) File.WriteAllText(Path.Combine(folder, $"file-{i}.txt"), $"content-{i}");
        var version = box.Version with { SourcePath = folder };
        box.Store.Save(box.Store.State with { Libraries = [box.Library with { Versions = [version] }] });
        string? initialJournal = null;
        var checks = 0;
        var gameChecks = 0;
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        {
            if (phase != "BeforeWrite") return;
            var file = Assert.Single(Directory.GetFiles(Path.Combine(box.Data, "operations"), "*.json"));
            var text = File.ReadAllText(file);
            initialJournal ??= text;
            Assert.Equal(initialJournal, text);
            var pending = AtomicJson.Read<OperationJournal>(file);
            Assert.Equal(pending.Files.Length - 1, pending.IntentIndex);
            Assert.Empty(Directory.GetFiles(Path.Combine(box.Data, "blobs")));
            checks++;
        }, _ => gameChecks++);
        var result = engine.Execute(box.Plan(new DeploymentRequest(box.Library.Id, version.Id, OperationKind.Deploy)), new(true, true));
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(result).Status);
        Assert.Equal(33, checks);
        Assert.Equal(1, gameChecks);
        Assert.Empty(Directory.GetFiles(Path.Combine(box.Data, "blobs")));
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(box.Run(new(box.Library.Id, null, OperationKind.Clear))).Status);
        Assert.Empty(Directory.GetFiles(Path.Combine(box.Data, "blobs")));
        Assert.Equal(33, Directory.GetFiles(folder).Length);
    }

    [Fact]
    public void Failed_clear_restores_managed_files_without_a_source_cache()
    {
        using var box = new Sandbox(); box.Deploy();
        File.Move(box.Source, box.Source + ".offline");
        var engine = new DeploymentEngine(box.Store, (phase, _) =>
        { if (phase == "AfterWrite") throw new IOException("fixture failure"); });
        var result = engine.Execute(box.Plan(new DeploymentRequest(box.Library.Id, null, OperationKind.Clear)), new(true, true));
        Assert.Equal(OperationStatus.Restored, Assert.Single(result).Status);
        Assert.Equal("v1", File.ReadAllText(box.Target));
        Assert.Single(box.Store.State.Receipts);
        Assert.Empty(Directory.GetFiles(Path.Combine(box.Data, "blobs")));
    }
}
