using Rhi.Core.Configuration;
using Rhi.Core.Models;
using Rhi.Core.Tests.Deployment;
using Rhi.Core.Tests.ReShade;

namespace Rhi.Core.Tests.Configuration;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_names_rejected_before_save(string name)
    { Assert.Equal("NameRequired", Assert.Throws<DeploymentException>(() => ConfigurationRules.Name(name)).Code); }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:\\absolute")]
    [InlineData("CON")]
    public void Unsafe_library_targets_rejected(string relative)
    {
        Assert.Equal("UnsafeRelativePath", Assert.Throws<DeploymentException>(() =>
            ConfigurationRules.Library(new(Guid.NewGuid(), "Library", relative, []))).Code);
    }

    [Fact]
    public void Version_validation_rejects_overlap_and_missing_sources_without_writing()
    {
        using var box = new Sandbox();
        Assert.Throws<DeploymentException>(() => ConfigurationRules.Version(box.Store.State, box.Data,
            box.Version with { SourcePath = box.Game.ExePath }));
        Assert.Throws<DeploymentException>(() => ConfigurationRules.Version(box.Store.State, box.Data,
            box.Version with { SourcePath = box.Source + ".missing" }));
        Assert.Equal("v1", File.ReadAllText(box.Source));
    }

    [Fact]
    public void Managed_game_paths_require_explicit_relocation()
    {
        using var box = new Sandbox();
        File.WriteAllBytes(box.Game.ExePath, ReShadeFixture.Pe(true, dll: false)); box.Deploy();
        var other = Path.Combine(box.Root, "other"); Directory.CreateDirectory(other);
        File.WriteAllBytes(Path.Combine(other, "game.exe"), ReShadeFixture.Pe(true, dll: false));
        Assert.Equal("ManagedLocationLocked", Assert.Throws<DeploymentException>(() => ConfigurationRules.Game(box.Store.State, box.Data,
            box.Game with { ExePath = Path.Combine(other, "game.exe"), InstallRoot = other, DeploymentRoot = other })).Code);
    }
}
