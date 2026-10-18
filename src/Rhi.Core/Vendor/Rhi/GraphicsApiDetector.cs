// Adapted from RankFTW/RHI, commit b0570336431bfce265e0f6dc19156ea751e404f7 (GPL-3.0).
// Copyright retained by the upstream authors and contributors; see LICENSE and THIRD-PARTY-NOTICES.md.
// Adaptation: retain DetectAllApis and PE helpers; omit disk/memory caches, labels and guessed defaults.
// Read with a stable handle; handle delay-only imports and represent DXGI as a family.
using System.Text;

namespace Rhi.Core.Vendor.Rhi;

public static class GraphicsApiDetector
{
    private const int HeaderBufferSize = 4096;
    private const int ImportReadSize = 65536;
    private static readonly Dictionary<string, GraphicsApiType> DllMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["d3d8.dll"]      = GraphicsApiType.DirectX8,
        ["d3d9.dll"]      = GraphicsApiType.DirectX9,
        ["d3d10.dll"]     = GraphicsApiType.DirectX10,
        ["d3d10_1.dll"]   = GraphicsApiType.DirectX10,
        ["d3d11.dll"]     = GraphicsApiType.DirectX11,
        ["d3d12.dll"]     = GraphicsApiType.DirectX12,
        ["vulkan-1.dll"]  = GraphicsApiType.Vulkan,
        ["opengl32.dll"]  = GraphicsApiType.OpenGL,
    };

    public static HashSet<GraphicsApiType> DetectAllApis(string exePath)
    {
        var result = new HashSet<GraphicsApiType>();

        if (string.IsNullOrEmpty(exePath))
            return result;

        try
        {
            using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Phase 1: Read headers (DOS header, PE header, section table)
            var header = new byte[HeaderBufferSize];
            int headerRead = stream.Read(header, 0, header.Length);

            if (headerRead < 0x40 || header[0] != (byte)'M' || header[1] != (byte)'Z')
                return result;

            int peOffset = BitConverter.ToInt32(header, 0x3C);
            if (peOffset < 0 || peOffset + 24 > headerRead)
                return result;

            if (header[peOffset] != (byte)'P' || header[peOffset + 1] != (byte)'E' ||
                header[peOffset + 2] != 0 || header[peOffset + 3] != 0)
                return result;

            int coffOffset = peOffset + 4;
            int numberOfSections = BitConverter.ToUInt16(header, coffOffset + 2);
            int sizeOfOptionalHeader = BitConverter.ToUInt16(header, coffOffset + 16);
            int optionalHeaderOffset = coffOffset + 20;

            if (optionalHeaderOffset + sizeOfOptionalHeader > headerRead)
                return result;

            ushort magic = BitConverter.ToUInt16(header, optionalHeaderOffset);
            int importDirOffset;
            if (magic == 0x10B) // PE32
                importDirOffset = optionalHeaderOffset + 104;
            else if (magic == 0x20B) // PE32+
                importDirOffset = optionalHeaderOffset + 120;
            else
                return result;

            if (importDirOffset + 8 > headerRead)
                return result;

            uint importRva = BitConverter.ToUInt32(header, importDirOffset);

            // Parse section table to build RVA-to-file-offset mapping
            int sectionTableOffset = optionalHeaderOffset + sizeOfOptionalHeader;
            var sections = new List<(uint va, uint vsize, uint rawPtr)>();
            for (int i = 0; i < numberOfSections; i++)
            {
                int secOff = sectionTableOffset + (i * 40);
                if (secOff + 40 > headerRead) break;
                uint va = BitConverter.ToUInt32(header, secOff + 12);
                uint vs = BitConverter.ToUInt32(header, secOff + 8);
                uint rp = BitConverter.ToUInt32(header, secOff + 20);
                sections.Add((va, vs, rp));
            }

            // Phase 2: Seek to import table and read it
            long importFileOffset = importRva == 0 ? 0 : RvaToFileOffset(sections, importRva);
            if (importFileOffset < 0)
                return result;

            stream.Seek(importFileOffset, SeekOrigin.Begin);
            var importBuf = new byte[ImportReadSize];
            int importRead = importRva == 0 ? 0 : stream.Read(importBuf, 0, importBuf.Length);

            // Walk Import Directory Table entries (each 20 bytes)
            bool importsDxgi = false;
            bool hasExplicitDx = false;

            for (int i = 0; ; i++)
            {
                int entryOffset = i * 20;
                if (entryOffset + 20 > importRead)
                    break;

                uint nameRva = BitConverter.ToUInt32(importBuf, entryOffset + 12);
                if (nameRva == 0)
                    break;

                string? dllName = ReadDllName(stream, sections, nameRva);
                if (dllName == null)
                    continue;

                if (dllName.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase))
                {
                    importsDxgi = true;
                    continue;
                }

                if (DllMap.TryGetValue(dllName, out var api))
                {
                    result.Add(api);
                    if (api is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9 or
                        GraphicsApiType.DirectX10 or GraphicsApiType.DirectX11 or GraphicsApiType.DirectX12)
                    {
                        hasExplicitDx = true;
                    }
                }
            }

            // DXGI identifies the DX10/11/12 family, not the exact runtime API.
            if (importsDxgi && !hasExplicitDx)
                result.UnionWith([GraphicsApiType.DirectX10, GraphicsApiType.DirectX11, GraphicsApiType.DirectX12]);

            // Scan delay-load import table — UE4/UE5 DX12 games delay-load d3d12.dll
            uint delayImportRvaAll = 0;
            if (magic == 0x10B)
                delayImportRvaAll = (optionalHeaderOffset + 200 + 4 <= headerRead) ? BitConverter.ToUInt32(header, optionalHeaderOffset + 200) : 0;
            else if (magic == 0x20B)
                delayImportRvaAll = (optionalHeaderOffset + 216 + 4 <= headerRead) ? BitConverter.ToUInt32(header, optionalHeaderOffset + 216) : 0;

            if (delayImportRvaAll != 0)
            {
                long delayFileOffsetAll = RvaToFileOffset(sections, delayImportRvaAll);
                if (delayFileOffsetAll >= 0)
                {
                    stream.Seek(delayFileOffsetAll, SeekOrigin.Begin);
                    var delayBufAll = new byte[ImportReadSize];
                    int delayReadAll = stream.Read(delayBufAll, 0, delayBufAll.Length);
                    for (int i = 0; ; i++)
                    {
                        int entryOff = i * 32;
                        if (entryOff + 32 > delayReadAll) break;
                        uint nameRvaD = BitConverter.ToUInt32(delayBufAll, entryOff + 4);
                        if (nameRvaD == 0) break;
                        string? dllNameD = ReadDllName(stream, sections, nameRvaD);
                        if (dllNameD != null && DllMap.TryGetValue(dllNameD, out var delayApiAll))
                            result.Add(delayApiAll);
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[GraphicsApiDetector] Error in DetectAllApis for '{exePath}': {ex.Message}");
            return result;
        }
    }

    private static string? ReadDllName(FileStream stream, List<(uint va, uint vsize, uint rawPtr)> sections, uint nameRva)
    {
        long nameFileOffset = RvaToFileOffset(sections, nameRva);
        if (nameFileOffset < 0)
            return null;

        long savedPos = stream.Position;
        try
        {
            stream.Seek(nameFileOffset, SeekOrigin.Begin);
            var nameBuf = new byte[256]; // DLL names are short
            int nameRead = stream.Read(nameBuf, 0, nameBuf.Length);
            if (nameRead == 0)
                return null;

            int end = 0;
            while (end < nameRead && nameBuf[end] != 0)
                end++;
            return Encoding.ASCII.GetString(nameBuf, 0, end);
        }
        finally
        {
            stream.Seek(savedPos, SeekOrigin.Begin);
        }
    }

    /// <summary>
    /// Converts an RVA to a file offset using the section table.
    /// Returns -1 if the RVA cannot be mapped.
    /// </summary>
    private static long RvaToFileOffset(List<(uint va, uint vsize, uint rawPtr)> sections, uint rva)
    {
        foreach (var (va, vsize, rawPtr) in sections)
        {
            if (rva >= va && rva < va + vsize)
                return rawPtr + (rva - va);
        }
        return -1;
    }
}
