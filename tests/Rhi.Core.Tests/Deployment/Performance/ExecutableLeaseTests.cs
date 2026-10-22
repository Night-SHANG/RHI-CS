using Rhi.Core.Models;

namespace Rhi.Core.Tests.Deployment;

public sealed class ExecutableLeaseTests
{
    [Fact]
    public void Executable_is_locked_through_each_write_and_released_after_commit()
    {
        Sandbox? box = null; var attempts = 0;
        using (box = new Sandbox((stage, _) =>
        {
            if (stage != "BeforeWrite") return;
            attempts++;
            Assert.Throws<IOException>(() => File.WriteAllText(box!.Game.ExePath, "changed"));
            Assert.Throws<IOException>(() => File.Move(box!.Game.ExePath, box.Game.ExePath + ".moved"));
            Assert.Throws<IOException>(() => Directory.Move(box!.Game.DeploymentRoot, box.Game.DeploymentRoot + ".moved"));
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
    public void Executable_change_after_preflight_prevents_any_write()
    {
        using var box = new Sandbox();
        var plan = box.Plan(new DeploymentRequest(box.Library.Id, box.Version.Id, OperationKind.Deploy));
        File.AppendAllText(box.Game.ExePath, "changed");
        Assert.Equal("ChangedSincePreflight", Assert.Throws<DeploymentException>(() => box.Engine.Execute(plan, new(true, true))).Code);
        Assert.False(File.Exists(box.Target));
        Assert.Empty(box.Store.State.Receipts);
    }
}
