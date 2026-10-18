using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Tests.Deployment;

public sealed class PathTests
{
    [Theory]
    [InlineData("../source")]
    [InlineData("a/../b")]
    [InlineData("a:stream")]
    [InlineData("CON.txt")]
    [InlineData("folder. /mod")]
    public void Unsafe_relative_paths_are_rejected(string path) =>
        Assert.Throws<DeploymentException>(() => PathPolicy.Relative(path));

    [Fact]
    public void Source_inside_target_is_rejected()
    {
        using var f = new Sandbox();
        var version = f.Version with { SourcePath = f.Game.DeploymentRoot };
        f.Store.Save(f.Store.State with { Libraries = [f.Library with { Versions = [version] }] });
        Assert.False(f.Plan(new DeploymentRequest(f.Library.Id, version.Id, OperationKind.Deploy)).CanExecute);
    }

    [Fact]
    public void Unknown_files_and_nonempty_directories_survive_clear()
    {
        using var f = new Sandbox();
        f.Store.Save(f.Store.State with { Libraries = [f.Library with { TargetRelative = "mods" }] });
        f.Deploy();
        var unknown = Path.Combine(f.Game.DeploymentRoot, "mods", "unknown.txt");
        File.WriteAllText(unknown, "keep");
        f.Run(new(f.Library.Id, null, OperationKind.Clear));
        Assert.Equal("keep", File.ReadAllText(unknown));
    }

    [Fact]
    public void Reparse_source_is_blocked()
    {
        using var f = new Sandbox();
        var link = Path.Combine(f.Root, "linked-source");
        // GitHub Windows runner grants symbolic-link privilege; do not silently skip this security check.
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(f.Source)!);
        Assert.Throws<DeploymentException>(() => PathPolicy.Full(link));
    }
}
