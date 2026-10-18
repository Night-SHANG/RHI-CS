using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Rhi.Core.Discovery;
using Rhi.Core.Models;
using Rhi.Core.Vendor.ReShade;

namespace Rhi.Core.Deployment.Resources.ReShade;

internal sealed class ReShadeAdapter : IResourceAdapter
{
    public ResourceMap Map(ResourceLibrary library, ResourceVersion version, GameEntry game)
    {
        if (PathPolicy.Relative(version.TargetOverride ?? library.TargetRelative).Length != 0)
            throw new DeploymentException("ReShadeTargetMustBeRoot");
        var proxy = game.Api switch
        {
            "DirectX 9" => "d3d9.dll",
            "DirectX 10/11/12" or "DirectX 10" or "DirectX 11" or "DirectX 12" => "dxgi.dll",
            "OpenGL" => "opengl32.dll",
            _ => throw new DeploymentException("ReShadeUnsupportedApi", game.Api)
        };
        var architecture = PeInspector.Architecture(game.ExePath);
        if (architecture is not ("x86" or "x64") || architecture != game.Architecture)
            throw new DeploymentException("ReShadeInvalidBinary", game.ExePath);

        var source = PathPolicy.Full(version.SourcePath);
        var extension = Path.GetExtension(source);
        if (!extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new DeploymentException("ReShadeInvalidBinary", source);
        var input = Read(source, SetupArchive.MaximumInstallerBytes, required: true)!;
        byte[] module;
        try
        {
            module = extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ? input :
                SetupArchive.Extract(input, architecture == "x64" ? "ReShade64.dll" : "ReShade32.dll");
        }
        catch (InvalidDataException) { throw new DeploymentException("ReShadeInvalidArchive", source); }
        ValidateModule(module, architecture, source);

        var configPath = PathPolicy.Target(game.DeploymentRoot, "ReShade.ini");
        var config = Read(configPath, ReShadeConfiguration.MaximumBytes, required: false);
        var prepared = ReShadeConfiguration.Prepare(config, game.DeploymentRoot);
        return new(new(PathPolicy.Comparer)
        {
            [proxy] = new(null, module),
            ["ReShade.ini"] = new(null, prepared.Content, prepared.Edits)
        }, [new(source, Hash(input)), new(configPath, config is null ? null : Hash(config))]);
    }

    private static byte[]? Read(string path, int maximum, bool required)
    {
        PathPolicy.CheckNoLinks(path);
        if (Directory.Exists(path)) throw new DeploymentException("FileDirectoryConflict", path);
        if (!File.Exists(path))
        {
            if (required) throw new DeploymentException("SourceUnavailable", path);
            return null;
        }
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > maximum) throw new DeploymentException("InspectionFailed", path);
        var bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void ValidateModule(byte[] bytes, string architecture, string source)
    {
        try
        {
            if (bytes.Length > SetupArchive.MaximumModuleBytes) throw new BadImageFormatException();
            using var input = new MemoryStream(bytes, writable: false);
            using var pe = new PEReader(input);
            var headers = pe.PEHeaders;
            if (headers.PEHeader is null || pe.HasMetadata ||
                !headers.CoffHeader.Characteristics.HasFlag(Characteristics.Dll) ||
                headers.CoffHeader.Machine != (architecture == "x64" ? Machine.Amd64 : Machine.I386) ||
                headers.PEHeader.Magic != (architecture == "x64" ? PEMagic.PE32Plus : PEMagic.PE32))
                throw new BadImageFormatException();
        }
        catch (BadImageFormatException) { throw new DeploymentException("ReShadeInvalidBinary", source); }
    }
}
