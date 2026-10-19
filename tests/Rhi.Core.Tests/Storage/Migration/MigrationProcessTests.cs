using System.Diagnostics;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Migration;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Storage.Migration;

public sealed class MigrationProcessTests
{
    [Theory]
    [InlineData("CopyStarted")]
    [InlineData("Verified")]
    [InlineData("Switched")]
    [InlineData("FileRemoved")]
    [InlineData("Cleaned")]
    public async Task Abrupt_exit_without_finally_can_be_resumed(string phase)
    {
        using var f = new Sandbox();
        var destination = Path.Combine(f.Root, "moved");
        var logPath = Path.Combine(f.Root, "migration.json");
        var pointerPath = Path.Combine(f.Root, "locator.json");
        AtomicJson.Write(pointerPath, f.Data);
        var record = WorkspaceMigration.Prepare(f.Store, destination, new(logPath));
        f.Store.Dispose();
        var worker = Path.Combine(AppContext.BaseDirectory, "CrashWorker", "Rhi.CrashWorker.dll");
        Assert.True(File.Exists(worker));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { worker, f.Data, "Migration:" + phase, logPath, pointerPath }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 97, $"Exit {process.ExitCode}: {await output}\n{await error}");
        using var reopened = WorkspaceStore.Open(AtomicJson.Read<string>(pointerPath), expectedId: record.WorkspaceId);
        WorkspaceMigration.Run(reopened, new(logPath), (root, _) => AtomicJson.Write(pointerPath, root));
        Assert.False(Directory.Exists(f.Data));
        Assert.Null(new MigrationLog(logPath).Pending());
        Assert.Equal(destination, AtomicJson.Read<string>(pointerPath));
        MigrationFiles.Verify(destination, record.Inventory);
    }
}
