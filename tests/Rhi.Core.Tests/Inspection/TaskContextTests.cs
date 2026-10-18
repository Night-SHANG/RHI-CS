using Rhi.App.Services.Operations;
using Rhi.Core.Models;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Inspection;

public sealed class TaskContextTests
{
    [Fact]
    public void Result_context_preserves_game_library_version_and_action_after_configuration_changes()
    {
        using var f = new Sandbox();
        var context = OperationContext.Capture(f.Store.State, f.Game.Id, [new(f.Library.Id, f.Version.Id, OperationKind.Deploy)]);
        f.Store.Save(f.Store.State with { Games = [f.Game with { Name = "renamed game" }],
            Libraries = [f.Library with { Name = "renamed library" }] });
        Assert.Equal("测试游戏", context.GameName);
        var item = Assert.Single(context.Resources);
        Assert.Equal("测试库", item.Name);
        Assert.Equal("v1", item.Version);
        Assert.Equal("Deploy", item.Action);
    }

    [Fact]
    public void Clear_and_recovery_context_use_receipt_version_when_source_version_is_not_requested()
    {
        using var f = new Sandbox();
        f.Deploy();
        var requests = new[] { new DeploymentRequest(f.Library.Id, null, OperationKind.Clear) };
        Assert.Equal("v1", Assert.Single(OperationContext.Capture(f.Store.State, f.Game.Id, requests).Resources).Version);
        Assert.Equal("Recover", Assert.Single(OperationContext.Capture(f.Store.State, f.Game.Id, requests, recovery: true).Resources).Action);
    }
}
