using System.Text;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Preparation;
using Rhi.Core.Storage;

namespace Rhi.Core.Tests.ReShade;

public sealed class OfflinePreparationTests
{
    [Theory]
    [InlineData("x64", "DirectX 10/11/12", "dxgi.dll")]
    [InlineData("x86", "DirectX 10/11/12", "dxgi.dll")]
    [InlineData("x64", "DirectX 9", "d3d9.dll")]
    [InlineData("x86", "DirectX 9", "d3d9.dll")]
    [InlineData("x64", "OpenGL", "opengl32.dll")]
    [InlineData("x86", "OpenGL", "opengl32.dll")]
    public void Export_is_read_only_to_source_and_can_deploy_and_clear_as_an_ordinary_library(string arch, string api, string dll)
    {
        using var f = new ReShadeFixture(installer: true);
        var box = f.Box;
        var before = AtomicJson.Fingerprint(box.Store.State);
        var source = File.ReadAllBytes(f.Source);
        var request = new OfflineReShadeRequest(f.Source, box.Root, "offline", arch, api);
        var output = OfflineReShade.Create(box.Store.State, box.Data, request);
        Assert.Equal(source, File.ReadAllBytes(f.Source));
        Assert.Equal(before, AtomicJson.Fingerprint(box.Store.State));
        Assert.False(File.Exists(Path.Combine(box.Game.DeploymentRoot, dll)));
        Assert.Equal(ReShadeFixture.Pe(arch == "x64"), File.ReadAllBytes(Path.Combine(output, dll)));
        Assert.Contains(@".\reshade-shaders\Shaders\**", File.ReadAllText(Path.Combine(output, "ReShade.ini"), Encoding.UTF8));
        Assert.Equal(2, Directory.GetFiles(output).Length);
        var version = f.Version with { SourcePath = output, Kind = ResourceKind.Files };
        box.Store.Save(box.Store.State with { Libraries = [box.Library with { Versions = [version] }] });
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(box.Run(new(box.Library.Id, version.Id, OperationKind.Deploy))).Status);
        Assert.Equal(File.ReadAllBytes(Path.Combine(output, dll)), File.ReadAllBytes(Path.Combine(box.Game.DeploymentRoot, dll)));
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(box.Run(new(box.Library.Id, null, OperationKind.Clear))).Status);
        Assert.False(File.Exists(Path.Combine(box.Game.DeploymentRoot, dll)));
        Assert.True(File.Exists(Path.Combine(output, dll)));
    }

    [Theory]
    [InlineData("Data")]
    [InlineData("Game")]
    [InlineData("Source")]
    public void Output_in_managed_or_source_directories_is_rejected(string location)
    {
        using var f = new ReShadeFixture(installer: true);
        var box = f.Box;
        var parent = location switch { "Data" => box.Data, "Game" => box.Game.DeploymentRoot, _ => Path.GetDirectoryName(f.Source)! };
        // Register the whole source folder to verify existing directory sources stay read-only.
        box.Store.Save(box.Store.State with { Libraries = [box.Library with { Versions = [box.Version with
            { SourcePath = Path.GetDirectoryName(f.Source)! }] }] });
        Assert.Throws<DeploymentException>(() => OfflineReShade.Create(box.Store.State, box.Data,
            new(f.Source, parent, "offline", "x64", "DirectX 9")));
        Assert.False(Directory.Exists(Path.Combine(parent, "offline")));
    }

    [Fact]
    public void Existing_output_is_never_overwritten()
    {
        using var f = new ReShadeFixture(installer: true);
        var directory = Path.Combine(f.Box.Root, "offline"); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "keep.txt"); File.WriteAllText(file, "keep", Encoding.UTF8);
        Assert.Throws<DeploymentException>(() => OfflineReShade.Create(f.Box.Store.State, f.Box.Data,
            new(f.Source, f.Box.Root, "offline", "x64", "OpenGL")));
        Assert.Equal("keep", File.ReadAllText(file, Encoding.UTF8));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("Vulkan", false)]
    [InlineData("OpenGL", true)]
    public void Unsupported_api_or_wrong_binary_does_not_publish_output(string api, bool wrongBinary)
    {
        using var f = new ReShadeFixture(installer: true);
        if (wrongBinary) File.WriteAllBytes(f.Source, ReShadeFixture.Archive(wrong64: true));
        Assert.Throws<DeploymentException>(() => OfflineReShade.Create(f.Box.Store.State, f.Box.Data,
            new(f.Source, f.Box.Root, "offline", "x64", api)));
        Assert.False(Directory.Exists(Path.Combine(f.Box.Root, "offline")));
        Assert.Empty(Directory.GetDirectories(f.Box.Root, ".rhi-offline-*"));
    }
}
