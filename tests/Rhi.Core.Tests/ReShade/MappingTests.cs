using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Vendor.ReShade;

namespace Rhi.Core.Tests.ReShade;

public sealed class MappingTests
{
    [Theory]
    [InlineData(false, "DirectX 9", false, "d3d9.dll")]
    [InlineData(true, "DirectX 10/11/12", false, "dxgi.dll")]
    [InlineData(true, "DirectX 12", true, "dxgi.dll")]
    [InlineData(false, "OpenGL", true, "opengl32.dll")]
    public void Local_DLL_and_official_appended_ZIP_use_the_same_deployment_plan(bool x64, string api, bool installer, string proxy)
    {
        using var f = new ReShadeFixture(x64, api, installer);
        var source = File.ReadAllBytes(f.Source);
        var plan = f.Box.Plan(f.Deploy);
        Assert.True(plan.CanExecute, string.Join(";", plan.Issues.Select(i => i.Detail)));
        var library = Assert.Single(plan.Libraries);
        Assert.Equal(2, library.Changes.Length);
        Assert.Equal(2, library.Inputs!.Length);
        var dll = Assert.Single(library.Changes.Where(c => c.RelativePath == proxy));
        Assert.Equal(ReShadeFixture.Pe(x64), dll.GeneratedContent);
        Assert.False(File.Exists(Path.Combine(f.Box.Game.DeploymentRoot, proxy)));
        Assert.False(File.Exists(f.Config));
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Box.Engine.Execute(plan, new(true, true))).Status);
        Assert.Equal(ReShadeFixture.Pe(x64), File.ReadAllBytes(Path.Combine(f.Box.Game.DeploymentRoot, proxy)));
        Assert.Equal(source, File.ReadAllBytes(f.Source));
        Assert.Equal(2, Assert.Single(f.Box.Store.State.Receipts).Files.Length);
    }

    [Theory]
    [InlineData("Vulkan")]
    [InlineData("Unknown")]
    public void Unsupported_API_is_blocked_without_writes(string api)
    {
        using var f = new ReShadeFixture(api: api);
        var plan = f.Box.Plan(f.Deploy);
        Assert.Contains(plan.Issues, i => i.Code == "ReShadeUnsupportedApi");
        Assert.False(File.Exists(f.Config));
        Assert.Empty(f.Box.Store.Pending());
    }

    [Fact]
    public void DLL_bitness_and_actual_game_bitness_must_match()
    {
        using var f = new ReShadeFixture();
        File.WriteAllBytes(f.Source, ReShadeFixture.Pe(false));
        Assert.Contains(f.Box.Plan(f.Deploy).Issues, i => i.Code == "ReShadeInvalidBinary");
        File.WriteAllBytes(f.Source, ReShadeFixture.Pe(true, dll: false));
        Assert.Contains(f.Box.Plan(f.Deploy).Issues, i => i.Code == "ReShadeInvalidBinary");
        File.WriteAllBytes(f.Source, ReShadeFixture.Pe(true));
        File.WriteAllBytes(f.Box.Game.ExePath, ReShadeFixture.Pe(false, dll: false));
        Assert.Contains(f.Box.Plan(f.Deploy).Issues, i => i.Code == "ReShadeInvalidBinary");
    }

    [Fact]
    public void Installer_entry_names_are_not_trusted_as_architecture()
    {
        using var f = new ReShadeFixture(installer: true);
        File.WriteAllBytes(f.Source, ReShadeFixture.Archive(wrong64: true));
        Assert.Contains(f.Box.Plan(f.Deploy).Issues, i => i.Code == "ReShadeInvalidBinary");
    }

    [Fact]
    public void Duplicate_entries_are_rejected_and_unrelated_paths_are_never_extracted()
    {
        Assert.Throws<InvalidDataException>(() => SetupArchive.Extract(ReShadeFixture.Archive("reshade64.dll"), "ReShade64.dll"));
        Assert.Equal(ReShadeFixture.Pe(true), SetupArchive.Extract(ReShadeFixture.Archive("../escape.dll"), "ReShade64.dll"));
        Assert.Throws<InvalidDataException>(() => SetupArchive.Extract([1, 2, 3], "ReShade64.dll"));
    }

    [Fact]
    public void ReShade_cannot_be_redirected_by_a_version_subfolder()
    {
        using var f = new ReShadeFixture();
        f.Box.Store.Save(f.Box.Store.State with { Libraries = [f.Box.Library with
            { Versions = [f.Version with { TargetOverride = "another-folder" }] }] });
        Assert.Contains(f.Box.Plan(f.Deploy).Issues, i => i.Code == "ReShadeTargetMustBeRoot");
    }

    [Fact]
    public void Installer_change_after_preflight_invalidates_the_plan()
    {
        using var f = new ReShadeFixture(installer: true);
        var plan = f.Box.Plan(f.Deploy);
        var bytes = File.ReadAllBytes(f.Source); bytes[0x60] ^= 1;
        File.WriteAllBytes(f.Source, bytes);
        var ex = Assert.Throws<DeploymentException>(() => f.Box.Engine.Execute(plan, new(true, true)));
        Assert.Equal("ChangedSincePreflight", ex.Code);
        Assert.False(File.Exists(f.Config));
    }

    [Fact]
    public void Altering_generated_payload_cannot_bypass_the_approved_hash()
    {
        using var f = new ReShadeFixture();
        var plan = f.Box.Plan(f.Deploy);
        plan.Libraries[0].Changes.First(c => c.RelativePath == "dxgi.dll").GeneratedContent![0] ^= 1;
        var error = Assert.Throws<DeploymentException>(() => f.Box.Engine.Execute(plan, new(true, true)));
        Assert.Equal("ChangedSincePreflight", error.Code);
        Assert.False(File.Exists(Path.Combine(f.Box.Game.DeploymentRoot, "dxgi.dll")));
        Assert.False(File.Exists(f.Config));
    }
}
