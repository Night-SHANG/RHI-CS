using System.Diagnostics;
using System.Text;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.Core.Tests.ReShade;

public sealed class ProcessRecoveryTests
{
    [Theory]
    [InlineData("TargetPublished")]
    [InlineData("BeforeCommit")]
    public async Task Restart_restores_generated_files_without_the_installer(string phase)
    {
        using var f = new ReShadeFixture(installer: true);
        File.WriteAllText(f.Config, "[GENERAL]\nPresetPath=before.ini", Encoding.UTF8);
        var before = File.ReadAllBytes(f.Config);
        var sourceHash = BlobStore.Hash(f.Source);
        f.Box.Store.Dispose();
        var worker = Path.Combine(AppContext.BaseDirectory, "CrashWorker", "Rhi.CrashWorker.dll");
        Assert.True(File.Exists(worker), worker);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(worker);
        start.ArgumentList.Add(f.Box.Data);
        start.ArgumentList.Add(phase);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 97, $"Exit {process.ExitCode}: {await output}\n{await error}");
        using var reopened = WorkspaceStore.Open(f.Box.Data);
        var library = reopened.State.Libraries.Single();
        reopened.Save(reopened.State with { Libraries = [library with { Versions =
            [f.Version with { SourcePath = Path.Combine(Path.GetDirectoryName(f.Source)!, "offline.exe") }] }] });
        var journal = Assert.Single(reopened.Pending());
        Assert.Equal(OperationStatus.Restored, new DeploymentEngine(reopened).Recover(journal.Id).Status);
        Assert.Equal(before, File.ReadAllBytes(f.Config));
        Assert.False(File.Exists(Path.Combine(f.Box.Game.DeploymentRoot, "dxgi.dll")));
        Assert.Equal(sourceHash, BlobStore.Hash(f.Source));
        Assert.Empty(reopened.Pending());
        Assert.Empty(reopened.State.Receipts);
        Assert.Empty(Directory.GetFiles(f.Box.Game.DeploymentRoot, ".rhi-*"));
    }
}
