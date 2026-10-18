using System.Text;
using Rhi.Core.Discovery;
using Rhi.Core.Vendor.Rhi;

namespace Rhi.Core.Tests.Discovery;

public sealed class GraphicsInspectionTests
{
    [Theory]
    [InlineData(false, "d3d9.dll", "DirectX 9")]
    [InlineData(true, "d3d12.dll", "DirectX 10/11/12")]
    [InlineData(true, "dxgi.dll", "DirectX 10/11/12")]
    [InlineData(false, "opengl32.dll", "OpenGL")]
    [InlineData(true, "vulkan-1.dll", "Vulkan")]
    [InlineData(false, "d3d8.dll", "Unknown")]
    public void Reads_upstream_PE_imports_and_maps_only_supported_families(bool x64, string dll, string expected)
    {
        var file = WritePe(x64, [dll], []);
        var before = File.ReadAllBytes(file);
        Assert.Equal(expected, GraphicsInspection.SuggestApi(file));
        Assert.Equal(x64 ? "x64" : "x86", PeInspector.Architecture(file));
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public void Delay_only_imports_are_detected()
    {
        var file = WritePe(true, [], ["d3d12.dll"]);
        Assert.Equal("DirectX 10/11/12", GraphicsInspection.SuggestApi(file));
    }

    [Fact]
    public void Different_proxy_families_require_manual_selection()
    {
        var file = WritePe(true, ["d3d11.dll"], ["vulkan-1.dll"]);
        Assert.Equal("Unknown", GraphicsInspection.SuggestApi(file));
    }

    [Fact]
    public void Dxgi_does_not_claim_an_exact_DirectX_version()
    {
        var apis = GraphicsApiDetector.DetectAllApis(WritePe(true, ["dxgi.dll"], []));
        Assert.Equal(3, apis.Count);
        Assert.Contains(GraphicsApiType.DirectX10, apis);
        Assert.Contains(GraphicsApiType.DirectX11, apis);
        Assert.Contains(GraphicsApiType.DirectX12, apis);
    }

    [Fact]
    public void Invalid_and_changed_executables_do_not_reuse_cached_suggestions()
    {
        var file = WritePe(true, ["d3d12.dll"], []);
        Assert.Equal("DirectX 10/11/12", GraphicsInspection.SuggestApi(file));
        var timestamp = File.GetLastWriteTimeUtc(file);
        File.WriteAllBytes(file, [0, 1, 2]);
        File.SetLastWriteTimeUtc(file, timestamp);
        Assert.Equal("Unknown", GraphicsInspection.SuggestApi(file));
    }

    private static string WritePe(bool x64, string[] imports, string[] delayed)
    {
        var bytes = new byte[8192];
        using var stream = new MemoryStream(bytes);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        void U16(int at, ushort value) { stream.Position = at; writer.Write(value); }
        void U32(int at, uint value) { stream.Position = at; writer.Write(value); }
        U16(0, 0x5A4D); U32(0x3C, 0x80); U32(0x80, 0x4550);
        U16(0x84, x64 ? (ushort)0x8664 : (ushort)0x14C); U16(0x86, 1);
        var optionalSize = x64 ? 240 : 224;
        U16(0x94, (ushort)optionalSize);
        const int optional = 0x98;
        U16(optional, x64 ? (ushort)0x20B : (ushort)0x10B);
        U32(optional + (x64 ? 120 : 104), imports.Length > 0 ? 0x1000u : 0u);
        U32(optional + (x64 ? 216 : 200), delayed.Length > 0 ? 0x1200u : 0u);
        var section = optional + optionalSize;
        U32(section + 8, 0x1000); U32(section + 12, 0x1000); U32(section + 20, 0x400);
        var nameOffset = 0x800;
        void Name(int descriptor, string name)
        {
            U32(descriptor, (uint)(nameOffset - 0x400 + 0x1000));
            stream.Position = nameOffset;
            writer.Write(Encoding.ASCII.GetBytes(name + "\0"));
            nameOffset += 128;
        }
        for (var i = 0; i < imports.Length; i++) Name(0x400 + i * 20 + 12, imports[i]);
        for (var i = 0; i < delayed.Length; i++)
        {
            U32(0x600 + i * 32, 1);
            Name(0x600 + i * 32 + 4, delayed[i]);
        }
        var root = Path.Combine(Path.GetTempPath(), "RhiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "game.exe");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
