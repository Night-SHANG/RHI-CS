using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Rhi.Core.Models;

namespace Rhi.Core.Storage.Windows;

// Thin bindings to Windows file APIs; deployment decisions remain in the executor.
internal static class NativeFile
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path,
        uint length, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, IntPtr data, uint size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RenameInfo
    {
        public uint ReplaceIfExists;
        public IntPtr RootDirectory;
        public uint FileNameLength;
        public char FirstCharacter;
    }

    internal static IOException Error(string operation) =>
        new(operation, new Win32Exception(Marshal.GetLastWin32Error()));

    internal static string FinalPath(SafeFileHandle handle)
    {
        // VOLUME_NAME_GUID removes drive aliases, including SUBST.
        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 1);
        if (length == 0) throw Error("GetFinalPathNameByHandle");
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 1);
            if (length == 0 || length >= buffer.Capacity) throw Error("GetFinalPathNameByHandle");
        }
        return Path.TrimEndingDirectorySeparator(buffer.ToString());
    }

    internal static FileStream Open(string path, bool create = false, bool buffered = false)
    {
        // DELETE + GENERIC_READ (+ GENERIC_WRITE for our new staging file).
        // No FILE_SHARE_WRITE or FILE_SHARE_DELETE: inspection and mutation use this handle.
        var handle = CreateFileW(path, create ? 0xC0010000u : 0x80010000u, 1,
            IntPtr.Zero, create ? 1u : 3u, buffered ? 0x00200000u : 0x80200000u, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Error("CreateFile"); handle.Dispose(); throw error; }
        try
        {
            FileIdentity.AssertSingleLink(handle, path);
            return new FileStream(handle, create ? FileAccess.ReadWrite : FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }

    internal static void Verify(FileStream file, string expected, string path)
    {
        file.Position = 0;
        if (Convert.ToHexString(SHA256.HashData(file)) != expected)
            throw new DeploymentException("ChangedSincePreflight", path);
        file.Position = 0;
    }

    internal static void Rename(FileStream file, string destination)
    {
        // FILE_RENAME_INFO, ReplaceIfExists = FALSE. Never overwrite a file that arrived later.
        var bytes = Encoding.Unicode.GetBytes(destination);
        var offset = Marshal.OffsetOf<RenameInfo>(nameof(RenameInfo.FirstCharacter)).ToInt32();
        var size = Math.Max(Marshal.SizeOf<RenameInfo>(), offset + bytes.Length + sizeof(char));
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, buffer, size);
            Marshal.StructureToPtr(new RenameInfo { FileNameLength = (uint)bytes.Length }, buffer, false);
            Marshal.Copy(bytes, 0, buffer + offset, bytes.Length);
            if (!SetFileInformationByHandle(file.SafeFileHandle, 3, buffer, (uint)size))
                throw Error("FileRenameInfo");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static void Delete(FileStream file)
    {
        var buffer = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(buffer, 1);
            if (!SetFileInformationByHandle(file.SafeFileHandle, 4, buffer, 1))
                throw Error("FileDispositionInfo");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
