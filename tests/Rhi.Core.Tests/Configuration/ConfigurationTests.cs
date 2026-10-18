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
    [InlineData("../outside", "TargetFolderInvalid")]
    [InlineData("C:\\absolute", "TargetFolderAbsolute")]
    [InlineData("F:\\resources\\offline", "TargetFolderAbsolute")]
    [InlineData("\\\\server\\share", "TargetFolderAbsolute")]
    [InlineData("/rooted", "TargetFolderAbsolute")]
    [InlineData("CON", "TargetFolderInvalid")]
    [InlineData("mods//textures", "TargetFolderInvalid")]
    [InlineData("mods ", "TargetFolderInvalid")]
    public void Unsafe_targets_explain_the_field_error_for_libraries_and_versions(string relative, string code)
    {
        Assert.Equal(code, Assert.Throws<DeploymentException>(() =>
            ConfigurationRules.Library(new(Guid.NewGuid(), "Library", relative, []))).Code);
        using var box = new Sandbox();
        Assert.Equal(code, Assert.Throws<DeploymentException>(() => ConfigurationRules.Version(box.Store.State, box.Data,
            box.Version with { TargetOverride = relative })).Code);
    }

    [Theory]
    [InlineData("", "SourcePathRequired")]
    [InlineData("   ", "SourcePathRequired")]
    [InlineData("mods", "SourcePathInvalid")]
    public void Source_input_explains_how_to_select_files(string source, string code)
    {
        using var box = new Sandbox();
        Assert.Equal(code, Assert.Throws<DeploymentException>(() => ConfigurationRules.Version(box.Store.State, box.Data,
            box.Version with { SourcePath = source })).Code);
    }

    [Fact]
    public void Source_errors_distinguish_missing_data_and_game_overlap()
    {
        using var box = new Sandbox();
        foreach (var (path, code) in new[] { (box.Source + ".missing", "SourcePathMissing"),
            (box.Data, "SourceOverlapsData"), (box.Game.InstallRoot, "SourceOverlapsGame"),
            (box.Game.ExePath, "SourceOverlapsGame") })
            Assert.Equal(code, Assert.Throws<DeploymentException>(() => ConfigurationRules.Version(box.Store.State, box.Data,
                box.Version with { SourcePath = path })).Code);
        Assert.Equal("v1", File.ReadAllText(box.Source));
        Assert.Empty(box.Store.State.Receipts);
    }

    [Theory]
    [InlineData(null, "library-folder")]
    [InlineData("mods", "mods")]
    public void Folder_versions_follow_library_or_use_their_own_game_subfolder(string? target, string expected)
    {
        using var box = new Sandbox();
        var version = ConfigurationRules.Version(box.Store.State, box.Data, box.Version with
        { SourcePath = Path.GetDirectoryName(box.Source)!, TargetOverride = target });
        var library = ConfigurationRules.Library(box.Library with { TargetRelative = "library-folder", Versions = [version] });
        box.Store.Save(box.Store.State with { Libraries = [library] });
        box.Deploy();
        Assert.Equal(ResourceKind.Files, version.Kind);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(box.Game.DeploymentRoot, expected, "mod.txt")));
        Assert.Equal("v1", File.ReadAllText(box.Source));
    }

    [Theory]
    [InlineData(ResourceKind.ReShadeStandard)]
    [InlineData(ResourceKind.ReShadeAddon)]
    public void Editing_legacy_versions_keeps_the_adapter_kind_and_source(ResourceKind kind)
    {
        using var fixture = new ReShadeFixture();
        var box = fixture.Box;
        var old = box.Store.State.Libraries[0].Versions[0] with { Kind = kind };
        var edited = ConfigurationRules.Version(box.Store.State, box.Data, old with { Label = "Renamed" });
        Assert.Equal(kind, edited.Kind);
        Assert.Equal(old.SourcePath, edited.SourcePath);
        Assert.Equal("", edited.TargetOverride);
        Assert.Equal(old.Id, edited.Id);
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
