using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Storage;

public sealed class PendingConfigurationTests
{
    public static IEnumerable<object[]> LockedEdits()
    {
        foreach (var status in new[] { OperationStatus.Preparing, OperationStatus.Executing, OperationStatus.NeedsRepair })
            foreach (var edit in new[] { "library-name", "library-target", "version-source", "remove-version", "remove-library", "game" })
                yield return [status, edit];
    }

    [Theory]
    [MemberData(nameof(LockedEdits))]
    public void Pending_operation_rejects_related_configuration_without_changing_ledger(OperationStatus status, string edit)
    {
        using var f = new Sandbox();
        Pending(f, status);
        var before = File.ReadAllBytes(Path.Combine(f.Data, "workspace.json"));
        var state = f.Store.State;
        var next = edit switch
        {
            "library-name" => state with { Libraries = [f.Library with { Name = "changed" }] },
            "library-target" => state with { Libraries = [f.Library with { TargetRelative = "changed" }] },
            "version-source" => state with { Libraries = [f.Library with { Versions = [f.Version with { SourcePath = f.Source + ".other" }] }] },
            "remove-version" => state with { Libraries = [f.Library with { Versions = [] }] },
            "remove-library" => state with { Libraries = [] },
            _ => state with { Games = [f.Game with { DeploymentRoot = Path.Combine(f.Root, "other-game") }] }
        };

        var error = Assert.Throws<DeploymentException>(() => f.Store.Save(next));

        Assert.Equal("PendingConfigurationLocked", error.Code);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.Data, "workspace.json")));
        Assert.Same(state, f.Store.State);
        Assert.Single(f.Store.Pending());
        Assert.False(File.Exists(f.Target));
    }

    [Fact]
    public void Unrelated_configuration_and_preferences_remain_editable()
    {
        using var f = new Sandbox();
        Pending(f, OperationStatus.NeedsRepair);
        var other = f.Library with { Id = Guid.NewGuid(), Name = "other" };
        f.Store.Save(f.Store.State with { Libraries = [f.Library, other], Preferences = new("Dark", "en-US") });
        f.Store.Save(f.Store.State with { Libraries = [f.Library, other with { Name = "renamed" }] });
        Assert.Equal("renamed", f.Store.State.Libraries[1].Name);
        Assert.Equal("en-US", f.Store.State.Preferences.Language);
        Assert.Single(f.Store.Pending());
    }

    [Fact]
    public void Successful_recovery_unlocks_configuration()
    {
        using var f = new Sandbox();
        var journal = Pending(f, OperationStatus.NeedsRepair);
        Assert.Equal(OperationStatus.Restored, f.Engine.Recover(journal.Id).Status);
        f.Store.Save(f.Store.State with { Libraries = [f.Library with { Name = "after recovery" }] });
        Assert.Empty(f.Store.Pending());
        Assert.Equal("after recovery", f.Store.State.Libraries[0].Name);
    }

    [Fact]
    public void Reopening_workspace_preserves_pending_configuration_lock()
    {
        using var f = new Sandbox();
        Pending(f, OperationStatus.Executing);
        f.Store.Dispose();
        using var reopened = WorkspaceStore.Open(f.Data);
        var error = Assert.Throws<DeploymentException>(() => reopened.Save(reopened.State with { Games = [] }));
        Assert.Equal("PendingConfigurationLocked", error.Code);
        Assert.Single(reopened.State.Games);
    }

    private static OperationJournal Pending(Sandbox f, OperationStatus status)
    {
        var journal = new OperationJournal(Guid.NewGuid(), f.Store.State.Id, f.Game.Id, f.Library.Id,
            OperationKind.Deploy, f.Game.DeploymentRoot, f.Game.ExePath, BlobStore.Hash(f.Game.ExePath)!,
            status, [], [], -1, null, DateTimeOffset.UtcNow, DirectoryLease.Inspect(f.Game.DeploymentRoot), FileProtocol: 1);
        f.Store.WriteJournal(journal);
        return journal;
    }
}
