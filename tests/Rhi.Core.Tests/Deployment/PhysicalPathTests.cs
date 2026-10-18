using System.ComponentModel;
using System.Runtime.InteropServices;
using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Tests.Deployment;

public sealed class PhysicalPathTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DefineDosDeviceW(uint flags, string device, string? target);

    [Fact]
    public void Drive_alias_cannot_bypass_source_target_isolation()
    {
        using var f = new Sandbox();
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var drive = Enumerable.Range('P', 11).Select(c => (char)c).First(c => !used.Contains(c)) + ":";
        var physical = @"\??\" + f.Game.DeploymentRoot;
        if (!DefineDosDeviceW(0x9, drive, physical)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            Assert.Throws<DeploymentException>(() => PathPolicy.Disjoint(drive + @"\", f.Game.DeploymentRoot));
            Assert.Throws<DeploymentException>(() => PathPolicy.Disjoint(drive + @"\new\mod.dll", f.Game.DeploymentRoot));
        }
        finally
        {
            if (!DefineDosDeviceW(0xF, drive, physical)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
