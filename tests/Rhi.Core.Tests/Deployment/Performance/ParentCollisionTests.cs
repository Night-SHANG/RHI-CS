using Rhi.Core.Models;

namespace Rhi.Core.Tests.Deployment;

public sealed class ParentCollisionTests
{
    [Theory]
    [InlineData("mod.txt", true)]
    [InlineData("MOD.TXT", true)]
    [InlineData("mod.txt-more", false)]
    [InlineData("mods", false)]
    public void Cross_library_parent_detection_uses_path_segments(string subfolder, bool conflict)
    {
        using var box = new Sandbox();
        var second = box.Library with { Id = Guid.NewGuid(), TargetRelative = subfolder };
        box.Store.Save(box.Store.State with { Libraries = [box.Library, second] });
        var plan = box.Plan(new(box.Library.Id, box.Version.Id, OperationKind.Deploy),
            new(second.Id, box.Version.Id, OperationKind.Deploy));
        Assert.Equal(conflict, plan.Issues.Any(i => i.Code == "FileDirectoryConflict"));
        Assert.Equal(!conflict, plan.CanExecute);
        Assert.False(File.Exists(box.Target));
    }
}
