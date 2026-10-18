using Rhi.Core.Models;
using Rhi.Core.Tests.Deployment;
using Rhi.Core.Tests.ReShade;

namespace Rhi.Core.Tests.Recovery;

public sealed class UnchangedFileTests
{
    [Fact]
    public void Same_bytes_are_adopted_with_consent_and_backup_without_rewriting_the_file()
    {
        using var f = new Sandbox();
        File.WriteAllText(f.Target, "v1");
        var timestamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(f.Target, timestamp);
        var plan = f.Plan(new DeploymentRequest(f.Library.Id, f.Version.Id, OperationKind.Deploy));
        Assert.True(plan.Libraries[0].RequiresOverwrite);
        Assert.Throws<DeploymentException>(() => f.Engine.Execute(plan, new(false, true)));
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Engine.Execute(plan, new(true, true))).Status);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(f.Target));
        var receipt = Assert.Single(f.Store.State.Receipts);
        Assert.NotNull(Assert.Single(receipt.Files).OriginalHash);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Run(new(f.Library.Id, null, OperationKind.Clear))).Status);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(f.Target));
        Assert.Equal("v1", File.ReadAllText(f.Target));
    }

    [Fact]
    public void Complete_existing_INI_keeps_its_timestamp_when_no_configuration_edit_is_needed()
    {
        using var f = new ReShadeFixture();
        File.WriteAllText(f.Config, "[GENERAL]\nEffectSearchPaths=.\\Shaders\\**\nTextureSearchPaths=.\\Textures\\**\nPresetPath=chosen.ini");
        var timestamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(f.Config, timestamp);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Box.Run(f.Deploy)).Status);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(f.Config));
        Assert.Contains("chosen.ini", File.ReadAllText(f.Config));
    }
}
