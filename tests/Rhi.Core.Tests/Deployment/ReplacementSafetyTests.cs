using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Tests.Deployment;

public sealed class ReplacementSafetyTests
{
    [Fact]
    public void Clear_does_not_require_the_original_source_volume_to_be_online()
    {
        using var f = new Sandbox();
        File.WriteAllText(f.Target, "original");
        f.Deploy();
        var offline = f.Version with { SourcePath = $@"\\?\Volume{{{Guid.NewGuid()}}}\source\mod.txt" };
        f.Store.Save(f.Store.State with { Libraries = [f.Library with { Versions = [offline] }] });
        var result = f.Run(new(f.Library.Id, null, OperationKind.Clear));
        Assert.Equal(OperationStatus.Succeeded, result.Single().Status);
        Assert.Equal("original", File.ReadAllText(f.Target));
    }

    [Fact]
    public void Copy_failure_before_publish_preserves_existing_target()
    {
        using var f = new Sandbox((phase, _) => { if (phase == "CopyCompleted") throw new IOException("copy failure"); });
        File.WriteAllText(f.Source, new string('x', 200000));
        File.WriteAllText(f.Target, "original");
        var result = f.Deploy().Single();
        Assert.Equal(OperationStatus.Restored, result.Status);
        Assert.Equal("original", File.ReadAllText(f.Target));
        Assert.Empty(Directory.GetFiles(f.Game.DeploymentRoot, ".rhi-*"));
        Assert.Empty(f.Store.State.Receipts);
    }

    [Fact]
    public void Clear_holds_the_verified_file_against_concurrent_writers_and_deletion()
    {
        using var f = new Sandbox();
        f.Deploy();
        var observed = false;
        var engine = new DeploymentEngine(f.Store, (phase, _) =>
        {
            if (phase != "TargetLocked") return;
            observed = true;
            Assert.Throws<IOException>(() => File.WriteAllText(f.Target, "another program"));
            Assert.Throws<IOException>(() => File.Delete(f.Target));
        });
        var result = engine.Execute(f.Plan(new DeploymentRequest(f.Library.Id, null, OperationKind.Clear)), new(true, true));
        Assert.True(observed);
        Assert.Equal(OperationStatus.Succeeded, result.Single().Status);
        Assert.False(File.Exists(f.Target));
        Assert.Empty(Directory.GetFiles(f.Game.DeploymentRoot, ".rhi-*"));
    }

    [Fact]
    public void File_arriving_in_the_rename_gap_is_never_overwritten()
    {
        using var f = new Sandbox();
        File.WriteAllText(f.Target, "original");
        var engine = new DeploymentEngine(f.Store, (phase, _) =>
        {
            if (phase == "TargetParked") File.WriteAllText(f.Target, "another program");
        });
        var result = engine.Execute(f.Plan(new DeploymentRequest(f.Library.Id, f.Version.Id, OperationKind.Deploy)), new(true, true));
        Assert.Equal(OperationStatus.NeedsRepair, result.Single().Status);
        Assert.Equal("another program", File.ReadAllText(f.Target));
        Assert.Single(f.Store.Pending());
        Assert.Equal("original", File.ReadAllText(Directory.GetFiles(f.Game.DeploymentRoot, "*.before").Single()));
    }

    [Fact]
    public void Replacing_game_directory_after_preflight_does_not_transfer_authority()
    {
        using var f = new Sandbox();
        var plan = f.Plan(new DeploymentRequest(f.Library.Id, f.Version.Id, OperationKind.Deploy));
        Directory.Move(f.Game.DeploymentRoot, f.Game.DeploymentRoot + ".old");
        Directory.CreateDirectory(f.Game.DeploymentRoot);
        File.WriteAllText(f.Game.ExePath, "test executable identity");
        Assert.Throws<DeploymentException>(() => f.Engine.Execute(plan, new(true, true)));
        Assert.False(File.Exists(f.Target));
    }

    [Fact]
    public void Failed_batch_recheck_keeps_success_and_reports_remaining_libraries()
    {
        using var f = new Sandbox();
        var second = f.Library with { Id = Guid.NewGuid(), Name = "B", TargetRelative = "B" };
        var third = f.Library with { Id = Guid.NewGuid(), Name = "C", TargetRelative = "C" };
        f.Store.Save(f.Store.State with { Libraries = [f.Library, second, third] });
        var plan = f.Plan(new DeploymentRequest(f.Library.Id, f.Version.Id, OperationKind.Deploy),
            new(second.Id, f.Version.Id, OperationKind.Deploy), new(third.Id, f.Version.Id, OperationKind.Deploy));
        var engine = new DeploymentEngine(f.Store, (phase, _) =>
        {
            if (phase == "LibraryCompleted")
                File.Move(Path.Combine(f.Data, "workspace.json"), Path.Combine(f.Data, "workspace.offline"));
        });
        var results = engine.Execute(plan, new(true, true));
        Assert.Equal(new[] { OperationStatus.Succeeded, OperationStatus.NotExecuted, OperationStatus.NotExecuted },
            results.Select(r => r.Status));
        Assert.Equal("v1", File.ReadAllText(f.Target));
    }
}
