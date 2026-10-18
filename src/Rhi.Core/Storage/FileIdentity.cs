using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Rhi.Core.Models;

namespace Rhi.Core.Storage;

public static class FileIdentity
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleInfo info);

    public static void AssertSingleLink(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        AssertSingleLink(file.SafeFileHandle, path);
    }
    public static void AssertSingleLink(SafeFileHandle handle, string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException("Cannot inspect file identity", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
            throw new DeploymentException("ReparsePoint", path);
        if (info.Links != 1) throw new DeploymentException("HardLinkTarget", path);
    }

    internal static string Directory(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException("Cannot inspect directory identity", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
            throw new DeploymentException("ReparsePoint", path);
        if ((info.Attributes & (uint)FileAttributes.Directory) == 0)
            throw new DeploymentException("FileDirectoryConflict", path);
        return $"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
    }
}
