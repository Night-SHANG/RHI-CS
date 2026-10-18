using System.IO.Compression;
using System.Text;
using Rhi.Core.Models;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.ReShade;

internal sealed class ReShadeFixture : IDisposable
{
    public Sandbox Box { get; }
    public string Source { get; }
    public string Config => Path.Combine(Box.Game.DeploymentRoot, "ReShade.ini");
    public ResourceVersion Version { get; }
    public DeploymentRequest Deploy => new(Box.Library.Id, Version.Id, OperationKind.Deploy);
    public DeploymentRequest Clear => new(Box.Library.Id, null, OperationKind.Clear);

    public ReShadeFixture(bool x64 = true, string api = "DirectX 10/11/12", bool installer = false,
        Action<string, int>? fault = null)
    {
        Box = new(fault);
        File.WriteAllBytes(Box.Game.ExePath, Pe(x64, dll: false));
        Source = Path.Combine(Path.GetDirectoryName(Box.Source)!, installer ? "setup.exe" : "ReShade.dll");
        File.WriteAllBytes(Source, installer ? Archive() : Pe(x64));
        Version = new(Guid.NewGuid(), "ReShade", Source, null, ResourceKind.ReShadeStandard);
        Box.Store.Save(Box.Store.State with
        {
            Games = [Box.Game with { Api = api, Architecture = x64 ? "x64" : "x86" }],
            Libraries = [Box.Library with { Versions = [Version] }]
        });
    }

    public static byte[] Pe(bool x64, bool dll = true)
    {
        var bytes = new byte[1024];
        using var stream = new MemoryStream(bytes);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        void U16(int at, ushort value) { stream.Position = at; writer.Write(value); }
        void U32(int at, uint value) { stream.Position = at; writer.Write(value); }
        U16(0, 0x5A4D); U32(0x3C, 0x80); U32(0x80, 0x4550);
        U16(0x84, x64 ? (ushort)0x8664 : (ushort)0x14C); U16(0x86, 1);
        var optionalSize = x64 ? 240 : 224;
        U16(0x94, (ushort)optionalSize); U16(0x96, dll ? (ushort)0x2002 : (ushort)2);
        const int optional = 0x98;
        U16(optional, x64 ? (ushort)0x20B : (ushort)0x10B);
        U32(optional + 32, 0x1000); U32(optional + 36, 0x200);
        U32(optional + 56, 0x2000); U32(optional + 60, 0x200);
        U16(optional + 68, 3); U32(optional + (x64 ? 108 : 92), 16);
        var section = optional + optionalSize;
        stream.Position = section; writer.Write(Encoding.ASCII.GetBytes(".text\0\0\0"));
        U32(section + 8, 0x200); U32(section + 12, 0x1000);
        U32(section + 16, 0x200); U32(section + 20, 0x200); U32(section + 36, 0x60000020);
        return bytes;
    }

    public static byte[] Archive(string? extraEntry = null, bool wrong64 = false)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in new[] { ("ReShade32.dll", Pe(false)), ("ReShade64.dll", Pe(!wrong64)) })
            {
                using var target = zip.CreateEntry(name).Open();
                target.Write(bytes);
            }
            if (extraEntry is not null)
            {
                using var target = zip.CreateEntry(extraEntry).Open();
                target.Write([1, 2, 3]);
            }
        }
        return [.. Pe(false, dll: false), .. output.ToArray()];
    }

    public void Dispose() => Box.Dispose();
}
