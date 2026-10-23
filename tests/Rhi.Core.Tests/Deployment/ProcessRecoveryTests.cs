using System.Diagnostics;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.Core.Tests.Deployment;

public sealed class ProcessRecoveryTests
{
    [Theory]
    [InlineData("StageReady")]
    [InlineData("TargetParked")]
    [InlineData("TargetPublished")]
    [InlineData("CopyCompleted")]
    [InlineData("BeforeWrite")]
    [InlineData("BeforeCommit")]
    public async Task A_new_process_restores_after_abrupt_exit(string phase)
    {
        using var f = new Sandbox();
        File.WriteAllText(f.Target, "original");
        f.Store.Dispose();
        var worker = Path.Combine(AppContext.BaseDirectory, "CrashWorker", "Rhi.CrashWorker.dll");
        Assert.True(File.Exists(worker), worker);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(worker);
        start.ArgumentList.Add(f.Data);
        start.ArgumentList.Add(phase);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 97, $"Exit {process.ExitCode}: {await output}\n{await error}");
        using var reopened = WorkspaceStore.Open(f.Data);
        var journal = Assert.Single(reopened.Pending());
        var result = new DeploymentEngine(reopened).Recover(journal.Id);
        Assert.Equal(OperationStatus.Restored, result.Status);
        Assert.Equal("original", File.ReadAllText(f.Target));
        Assert.Equal("v1", File.ReadAllText(f.Source));
        Assert.Empty(reopened.Pending());
        Assert.Empty(reopened.State.Receipts);
        Assert.Empty(Directory.GetFiles(f.Game.DeploymentRoot, ".rhi-*"));
    }
}
