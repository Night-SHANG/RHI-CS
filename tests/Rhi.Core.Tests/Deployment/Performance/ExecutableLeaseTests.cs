using Rhi.Core.Models;

namespace Rhi.Core.Tests.Deployment;

public sealed class ExecutableLeaseTests
{
    [Fact]
    public void Normal_copy_does_not_lock_or_rehash_the_game_executable()
    {
        Sandbox? box = null; var attempts = 0;
        using (box = new Sandbox((stage, _) =>
        {
            if (stage != "BeforeWrite") return;
            attempts++;
            File.WriteAllText(box!.Game.ExePath, "game updated");
        }))
        {
            Assert.Equal(OperationStatus.Succeeded, Assert.Single(box.Deploy()).Status);
            Assert.Equal(1, attempts);
            File.AppendAllText(box.Game.ExePath, "now-unlocked");
        }
    }

    [Fact]
    public void Executable_lock_is_released_before_rollback_and_after_failure()
    {
        using var box = new Sandbox((stage, _) => { if (stage == "AfterWrite") throw new IOException("fixture failure"); });
        File.WriteAllText(box.Target, "original");
        Assert.Equal(OperationStatus.Restored, Assert.Single(box.Deploy()).Status);
        Assert.Equal("original", File.ReadAllText(box.Target));
        File.AppendAllText(box.Game.ExePath, "lock-released");
    }

    [Fact]
    public void Executable_update_does_not_invalidate_an_unrelated_resource_copy()
    {
        using var box = new Sandbox();
        var plan = box.Plan(new DeploymentRequest(box.Library.Id, box.Version.Id, OperationKind.Deploy));
        File.AppendAllText(box.Game.ExePath, "changed");
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(box.Engine.Execute(plan, new(true, true))).Status);
        Assert.Equal("v1", File.ReadAllText(box.Target));
        Assert.Single(box.Store.State.Receipts);
    }
}
