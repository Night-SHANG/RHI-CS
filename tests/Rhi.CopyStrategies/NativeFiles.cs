using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Rhi.CopyStrategies;

// Microbenchmark-only bindings. Open/identity/rename mirror Core's NativeFile
// and FileIdentity at ec36f4e. This is not a deployment or recovery implementation.
internal static class NativeFiles
{
    private const uint ReadDelete = 0x80010000;
    private const uint ReadWriteDelete = 0xC0010000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint WriteThrough = 0x80000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleInfo info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, IntPtr data, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RenameInfo
    {
        public uint ReplaceIfExists;
        public IntPtr RootDirectory;
        public uint FileNameLength;
        public char FirstCharacter;
    }

    internal static FileStream Open(string path, bool create, bool writeThrough)
    {
        // CREATE_NEW / OPEN_EXISTING; only FILE_SHARE_READ, never write/delete.
        var handle = CreateFileW(path, create ? ReadWriteDelete : ReadDelete, 1,
            IntPtr.Zero, create ? 1u : 3u,
            OpenReparsePoint | (writeThrough ? WriteThrough : 0u), IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Error("CreateFile");
            handle.Dispose();
            throw error;
        }
        try
        {
            if (!GetFileInformationByHandle(handle, out var info)) throw Error("GetFileInformationByHandle");
            if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || info.Links != 1)
                throw new IOException("Disposable fixture is not a single-link ordinary file.");
            return new FileStream(handle, create ? FileAccess.ReadWrite : FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }

    internal static void Rename(FileStream file, string destination)
    {
        // Same-volume, no-overwrite, exact-handle rename, identical for all variants.
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

    private static IOException Error(string operation) =>
        new(operation, new Win32Exception(Marshal.GetLastWin32Error()));
}
