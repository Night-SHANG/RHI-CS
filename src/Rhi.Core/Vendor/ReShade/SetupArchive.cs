/*
 * Copyright (C) 2014 Patrick Mours
 * SPDX-License-Identifier: BSD-3-Clause
 */
// Adapted from crosire/reshade@7bf9de8b33bcc76c3177007e65d73c72dd0f34c0,
// setup/MainWindow.xaml.cs, InstallStep_InstallReShadeModule.
// Retains the 512-byte aligned appended-ZIP search and required DLL entries.
// Changes: caller-owned bytes, bounded extraction, unique entries, no install side effects.
using System.IO.Compression;

namespace Rhi.Core.Vendor.ReShade;

public static class SetupArchive
{
    public const int MaximumInstallerBytes = 128 * 1024 * 1024;
    public const int MaximumModuleBytes = 64 * 1024 * 1024;

    public static byte[] Extract(byte[] installer, string entryName)
    {
        if (installer.Length > MaximumInstallerBytes || entryName is not ("ReShade32.dll" or "ReShade64.dll"))
            throw new InvalidDataException("Invalid ReShade archive input.");
        using var input = new MemoryStream(installer, writable: false);
        using var output = new MemoryStream();
        var block = new byte[512];
        byte[] signature = [0x50, 0x4B, 0x03, 0x04];
        int count;
        while ((count = input.Read(block, 0, block.Length)) >= signature.Length)
        {
            if (count >= 30 && block.Take(signature.Length).SequenceEqual(signature) &&
                block.Skip(signature.Length).Take(26).Max() != 0)
            {
                output.Write(block, 0, count);
                input.CopyTo(output);
                break;
            }
        }
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var name in new[] { "ReShade32.dll", "ReShade64.dll" })
            if (zip.Entries.Count(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase)) != 1 ||
                zip.GetEntry(name) is null)
                throw new InvalidDataException("Missing or ambiguous ReShade DLL entry.");
        var entry = zip.GetEntry(entryName)!;
        if (entry.Length <= 0 || entry.Length > MaximumModuleBytes)
            throw new InvalidDataException("Invalid ReShade DLL size.");
        using var source = entry.Open();
        var bytes = new byte[checked((int)entry.Length)];
        source.ReadExactly(bytes);
        if (source.ReadByte() != -1) throw new InvalidDataException("ReShade DLL exceeds declared size.");
        return bytes;
    }
}
