// Adapted from RankFTW/RHI PeHeaderService.cs (GPL-3.0), blob a2a7fcb29737075de7b26a9a28dd7c36fca0abd4.
// Changes: seek to bounded PE offset, do not guess an EXE, no legacy logger or UI dependency.
using Rhi.Core.Deployment;

namespace Rhi.Core.Discovery;

public static class PeInspector
{
    public static string Architecture(string path)
    {
        PathPolicy.CheckNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) return "Unknown";
        stream.Position = 0x3C;
        var offset = reader.ReadInt32();
        if (offset < 64 || offset > stream.Length - 24) return "Unknown";
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x00004550) return "Unknown";
        return reader.ReadUInt16() switch { 0x014C => "x86", 0x8664 => "x64", 0xAA64 => "ARM64", _ => "Unknown" };
    }
}
