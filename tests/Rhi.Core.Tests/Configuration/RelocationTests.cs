using Rhi.Core.Configuration;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Tests.Deployment;
using Rhi.Core.Tests.ReShade;

namespace Rhi.Core.Tests.Configuration;

public sealed class RelocationTests
{
    private static Sandbox Deployed()
    {
        var box = new Sandbox();
        File.WriteAllBytes(box.Game.ExePath, ReShadeFixture.Pe(true, dll: false));
        File.WriteAllText(box.Target, "original");
        box.Deploy();
        return box;
    }
    private static string Move(Sandbox box)
    {
        var target = Path.Combine(box.Root, "relocated");
        Directory.Move(box.Game.DeploymentRoot, target);
        return target;
    }
    private static RelocationPlan Inspect(Sandbox box, string target) => GameRelocation.Inspect(box.Store,
        box.Game.Id, Path.Combine(target, "game.exe"), target, target);

    [Fact]
    public void Readonly_preview_then_atomic_relocation_survives_reopen_and_restores_original()
    {
        using var box = Deployed();
        var source = File.ReadAllBytes(box.Source);
        var ledger = File.ReadAllBytes(Path.Combine(box.Data, "workspace.json"));
        var target = Move(box);
        File.WriteAllText(Path.Combine(target, "user.txt"), "keep");
        var plan = Inspect(box, target);
        Assert.True(plan.CanApply);
        Assert.Equal(ledger, File.ReadAllBytes(Path.Combine(box.Data, "workspace.json")));
        GameRelocation.Apply(box.Store, plan);
        Assert.Equal(target, box.Store.State.Games.Single().DeploymentRoot);
        Assert.Equal(target, box.Store.State.Receipts.Single().TargetRoot);
        box.Store.Dispose();
        using var reopened = WorkspaceStore.Open(box.Data);
        var clear = new DeploymentPlanner(reopened).Build(reopened.State, box.Game.Id, [new(box.Library.Id, null, OperationKind.Clear)]);
        Assert.True(clear.CanExecute);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(new DeploymentEngine(reopened).Execute(clear, new(true, true))).Status);
        Assert.Equal("original", File.ReadAllText(Path.Combine(target, "mod.txt")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "user.txt")));
        Assert.Equal(source, File.ReadAllBytes(box.Source));
        Assert.False(Directory.Exists(box.Game.DeploymentRoot));
    }

    [Theory]
    [InlineData("modified")]
    [InlineData("missing")]
    [InlineData("backup")]
    [InlineData("old-returned")]
    public void Unverifiable_candidate_keeps_original_ledger(string change)
    {
        using var box = Deployed();
        var ledger = File.ReadAllBytes(Path.Combine(box.Data, "workspace.json"));
        var target = Move(box);
        if (change == "modified") File.WriteAllText(Path.Combine(target, "mod.txt"), "external");
        if (change == "missing") File.Move(Path.Combine(target, "mod.txt"), Path.Combine(target, "mod.offline"));
        if (change == "backup") File.WriteAllText(box.Store.Blobs.PathFor(box.Store.State.Receipts.Single().Files.Single().OriginalHash!), "corrupt");
        if (change == "old-returned") Directory.CreateDirectory(box.Game.DeploymentRoot);
        Assert.False(Inspect(box, target).CanApply);
        Assert.Equal(ledger, File.ReadAllBytes(Path.Combine(box.Data, "workspace.json")));
    }

    [Fact]
    public void Updated_executable_does_not_block_relocating_unchanged_managed_files()
    {
        using var box = Deployed();
        var target = Move(box);
        File.AppendAllText(Path.Combine(target, "game.exe"), "updated");
        var plan = Inspect(box, target);
        Assert.True(plan.CanApply);
        GameRelocation.Apply(box.Store, plan);
        Assert.Equal(target, box.Store.State.Receipts.Single().TargetRoot);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(target, "mod.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Changed_after_preview_cannot_commit(bool changeLedger)
    {
        using var box = Deployed();
        var target = Move(box); var plan = Inspect(box, target);
        Assert.True(plan.CanApply);
        if (changeLedger) box.Store.Save(box.Store.State with { Preferences = new("Dark", "en-US") });
        else File.WriteAllText(Path.Combine(target, "mod.txt"), "external");
        Assert.Equal("ChangedSincePreflight", Assert.Throws<DeploymentException>(() => GameRelocation.Apply(box.Store, plan)).Code);
        Assert.Equal(box.Game.DeploymentRoot, box.Store.State.Games.Single().DeploymentRoot);
    }

    [Fact]
    public void Candidate_files_stay_locked_through_final_commit_check()
    {
        using var box = Deployed(); var target = Move(box); var plan = Inspect(box, target);
        Assert.Throws<IOException>(() => GameRelocation.Apply(box.Store, plan,
            _ => File.WriteAllText(Path.Combine(target, "mod.txt"), "race")));
        Assert.Equal(box.Game.DeploymentRoot, box.Store.State.Receipts.Single().TargetRoot);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(target, "mod.txt")));
    }

    [Fact]
    public void Pending_recovery_blocks_relocation()
    {
        using var box = Deployed();
        var receipt = box.Store.State.Receipts.Single();
        box.Store.WriteJournal(new(Guid.NewGuid(), box.Store.State.Id, box.Game.Id, box.Library.Id, OperationKind.Clear,
            box.Game.DeploymentRoot, box.Game.ExePath, receipt.ExeHash, OperationStatus.NeedsRepair,
            [], [], -1, "test", DateTimeOffset.UtcNow, receipt.TargetIdentity));
        var result = Inspect(box, Move(box));
        Assert.Equal("PendingConfigurationLocked", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void A_copied_matching_tree_does_not_inherit_empty_directory_cleanup_rights()
    {
        using var box = Deployed();
        box.Store.Save(box.Store.State with { Receipts = [box.Store.State.Receipts.Single() with { CreatedDirectories = ["owned"] }] });
        Directory.CreateDirectory(Path.Combine(box.Game.DeploymentRoot, "owned"));
        var old = Move(box);
        var target = Path.Combine(box.Root, "copy"); Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(old)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        Directory.CreateDirectory(Path.Combine(target, "owned"));
        GameRelocation.Apply(box.Store, Inspect(box, target));
        Assert.Empty(box.Store.State.Receipts.Single().CreatedDirectories);
    }
}
