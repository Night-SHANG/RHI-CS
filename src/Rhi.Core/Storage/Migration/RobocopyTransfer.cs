using System.Diagnostics;
using Rhi.Core.Models;

namespace Rhi.Core.Storage.Migration;

public static class RobocopyTransfer
{
    public static void Copy(string source, string destination, Action? started = null)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "robocopy.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { source, destination, "/E", "/COPY:DAT", "/DCOPY:DAT", "/R:0", "/W:0",
            "/XJ", "/SL", "/IS", "/IT", "/NP", "/NFL", "/NDL", "/XF", Path.Combine(source, "workspace.lock") })
            start.ArgumentList.Add(argument);
        using var job = new TransferJob();
        using var process = Process.Start(start) ?? throw new DeploymentException("MigrationCopyFailed");
        job.Attach(process);
        started?.Invoke();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        // Robocopy uses a bit field, not the conventional zero-only success convention.
        if (process.ExitCode >= 8) throw new DeploymentException("MigrationCopyFailed", process.ExitCode.ToString());
    }
}
