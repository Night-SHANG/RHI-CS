using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Storage.Migration;

// Windows owns child lifetime after attachment; a crashed UI must not leave a copy running indefinitely.
internal sealed class TransferJob : IDisposable
{
    private readonly SafeFileHandle handle;
    [StructLayout(LayoutKind.Sequential)]
    private struct Limits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public nuint Affinity;
        public uint Priority, Scheduling;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref Limits limits, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);

    public TransferJob()
    {
        handle = CreateJobObjectW(0, null);
        if (handle.IsInvalid) { handle.Dispose(); throw NativeFile.Error("Create copy job"); }
        var limits = new Limits { Flags = 0x2000 }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<Limits>()))
        { handle.Dispose(); throw NativeFile.Error("Configure copy job"); }
    }
    public void Attach(Process process)
    {
        if (AssignProcessToJobObject(handle, process.Handle)) return;
        if (process.HasExited) return;
        var error = NativeFile.Error("Attach copy job");
        process.Kill(); process.WaitForExit();
        throw error;
    }
    public void Dispose() => handle.Dispose();
}
