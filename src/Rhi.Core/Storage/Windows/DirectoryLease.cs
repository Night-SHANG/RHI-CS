using Microsoft.Win32.SafeHandles;
using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Storage.Windows;

public sealed class DirectoryLease : IDisposable
{
    private readonly List<SafeFileHandle> handles = [];
    public DirectoryIdentity Identity { get; private set; } = null!;

    private DirectoryLease() { }

    public static DirectoryLease Acquire(string directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        PathPolicy.CheckNoLinks(directory);
        var lease = new DirectoryLease();
        try
        {
            var paths = new Stack<string>();
            for (string? path = Path.GetFullPath(directory); path is not null; path = Path.GetDirectoryName(path))
                paths.Push(path);
            while (paths.TryPop(out var path))
            {
                // FILE_READ_ATTRIBUTES, share read/write but NOT delete; open the directory itself.
                var handle = NativeFile.CreateFileW(path, 0x80, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { var error = NativeFile.Error("Open directory"); handle.Dispose(); throw error; }
                lease.handles.Add(handle);
                var identity = FileIdentity.Directory(handle, path);
                lease.Identity = new(NativeFile.FinalPath(handle), identity);
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    public static DirectoryIdentity Inspect(string directory)
    {
        using var lease = Acquire(directory);
        return lease.Identity;
    }

    public static DirectoryIdentity ExistingParent(string file)
    {
        var parent = Path.GetDirectoryName(file);
        while (parent is not null && !Directory.Exists(parent)) parent = Path.GetDirectoryName(parent);
        return Inspect(parent ?? throw new DirectoryNotFoundException(file));
    }

    // Missing leaf paths still resolve through their nearest existing parent.
    public static string PhysicalPath(string path)
    {
        PathPolicy.CheckNoLinks(path);
        var suffix = new Stack<string>();
        var parent = Path.GetFullPath(path);
        while (!Directory.Exists(parent))
        {
            suffix.Push(Path.GetFileName(parent));
            parent = Path.GetDirectoryName(parent) ?? throw new DirectoryNotFoundException(path);
        }
        using var lease = Acquire(parent);
        var result = lease.Identity.Path;
        while (suffix.TryPop(out var part)) result = Path.Combine(result, part);
        return result;
    }

    public void Dispose()
    {
        for (var i = handles.Count - 1; i >= 0; i--) handles[i].Dispose();
        handles.Clear();
    }
}
