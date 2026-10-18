using Rhi.Core.Deployment;
using Rhi.Core.Inspection;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Inspection;

public sealed class ObservationTests
{
    [Fact]
    public void Inspection_reports_modified_and_missing_files_without_mutating_targets_or_ledger()
    {
        using var f = new Sandbox();
        f.Deploy();
        var ledger = File.ReadAllBytes(Path.Combine(f.Data, "workspace.json"));
        File.WriteAllText(f.Target, "external");
        var modified = Assert.Single(DeploymentInspection.Inspect(f.Store, f.Game.Id).Receipts);
        Assert.Equal(TargetHealth.TargetModified, modified.Health);
        Assert.Equal("external", File.ReadAllText(f.Target));
        File.Move(f.Target, f.Target + ".external");
        var missing = Assert.Single(DeploymentInspection.Inspect(f.Store, f.Game.Id).Receipts);
        Assert.Equal(TargetHealth.TargetMissing, missing.Health);
        Assert.False(File.Exists(f.Target));
        Assert.Equal(ledger, File.ReadAllBytes(Path.Combine(f.Data, "workspace.json")));
    }

    [Fact]
    public void Source_loss_does_not_change_target_health()
    {
        using var f = new Sandbox();
        f.Deploy();
        File.Move(f.Source, f.Source + ".offline");
        Assert.Equal(TargetHealth.TargetMatched, Assert.Single(DeploymentInspection.Inspect(f.Store, f.Game.Id).Receipts).Health);
    }

    [Fact]
    public void Changed_game_identity_is_not_reported_as_matching()
    {
        using var f = new Sandbox();
        f.Deploy();
        File.AppendAllText(f.Game.ExePath, "updated");
        var result = Assert.Single(DeploymentInspection.Inspect(f.Store, f.Game.Id).Receipts);
        Assert.Equal(TargetHealth.TargetUnavailable, result.Health);
        Assert.Equal("GameIdentityChanged", Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Referenced_configuration_cannot_be_removed(bool wholeLibrary)
    {
        using var f = new Sandbox();
        f.Deploy();
        var before = File.ReadAllBytes(Path.Combine(f.Data, "workspace.json"));
        var next = f.Store.State with { Libraries = wholeLibrary ? [] : [f.Library with { Versions = [] }] };
        Assert.Equal("ConfigurationReferenced", Assert.Throws<DeploymentException>(() => f.Store.Save(next)).Code);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.Data, "workspace.json")));
        Assert.Equal("v1", File.ReadAllText(f.Target));
    }

    [Fact]
    public void Removing_unreferenced_configuration_never_removes_source_files()
    {
        using var f = new Sandbox();
        f.Store.Save(f.Store.State with { Libraries = [] });
        Assert.Equal("v1", File.ReadAllText(f.Source));
    }

    [Fact]
    public void Protection_inventory_retains_operation_identity_and_counts_all_protection_files()
    {
        using var f = new Sandbox();
        f.Deploy();
        File.WriteAllText(f.Target, "external");
        var result = Assert.Single(f.Run(new(f.Library.Id, null, OperationKind.Clear)));
        var inventory = ProtectionCatalog.Read(f.Store);
        var copy = Assert.Single(inventory.Operations);
        Assert.Equal(result.OperationId, copy.Entry.OperationId);
        Assert.Equal(f.Game.Id, copy.Entry.GameId);
        Assert.Equal(f.Library.Id, copy.Entry.LibraryId);
        f.Store.Save(f.Store.State with { Libraries = [] });
        Assert.Equal("测试游戏", copy.Entry.GameName);
        Assert.Equal("测试库", copy.Entry.LibraryName);
        Assert.Equal("external", File.ReadAllText(Path.Combine(copy.Directory, "mod.txt")));
        var actualBytes = Directory.GetFiles(Path.Combine(f.Data, "protection"), "*", SearchOption.AllDirectories)
            .Sum(p => new FileInfo(p).Length);
        Assert.Equal(actualBytes, inventory.Bytes);
        Assert.Empty(f.Store.State.Receipts);
    }
}
