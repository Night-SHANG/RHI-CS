using System.Security.Cryptography;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Storage;

public sealed class BlobCaptureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(131071)]
    [InlineData(131072)]
    [InlineData(131073)]
    [InlineData(400000)]
    public void Capture_reads_source_and_backup_once_and_persists_before_return(int length)
    {
        using var box = new Sandbox();
        var bytes = new byte[length];
        new Random(73).NextBytes(bytes);
        File.WriteAllBytes(box.Target, bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        BlobCaptureMeasurement? measured = null;
        box.Store.Blobs.CaptureObserver = value => measured = value;
        box.Store.Blobs.Capture(box.Target, hash);
        Assert.Equal(bytes, File.ReadAllBytes(box.Store.Blobs.PathFor(hash)));
        Assert.Equal(bytes, File.ReadAllBytes(box.Target));
        Assert.NotNull(measured);
        Assert.True(measured.Succeeded);
        Assert.Equal(length, measured.SourceBytesRead);
        Assert.Equal(length, measured.BackupBytesRead);
        Assert.Equal(length, measured.BytesWritten);
        Assert.Equal(2L * length, measured.BytesHashed);
        Assert.Equal(2, measured.HashPasses);
        Assert.Equal(2, measured.FlushCalls);
        Assert.Equal(1, measured.PublishedBlobs);
        Assert.False(measured.WriteThrough);
        Assert.Empty(Directory.GetFiles(Path.Combine(box.Data, "blobs"), "*.pending"));
    }

    [Fact]
    public void Cache_hit_verifies_without_rewriting_or_opening_source()
    {
        using var box = new Sandbox();
        var hash = BlobStore.Hash(box.Source)!;
        box.Store.Blobs.Capture(box.Source, hash);
        var path = box.Store.Blobs.PathFor(hash);
        var stamp = File.GetLastWriteTimeUtc(path);
        BlobCaptureMeasurement? measured = null;
        box.Store.Blobs.CaptureObserver = value => measured = value;
        box.Store.Blobs.Capture(Path.Combine(box.Root, "absent"), hash);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(1, measured!.ReusedBlobs);
        Assert.Equal(0, measured.SourceBytesRead);
        Assert.Equal(0, measured.BytesWritten);
        Assert.Equal(0, measured.FlushCalls);
        File.WriteAllText(path, "corrupted immutable blob");
        Assert.Throws<DeploymentException>(() => box.Store.Blobs.Capture(box.Source, hash));
        Assert.Equal("corrupted immutable blob", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("CopyWriting")]
    [InlineData("BeforePayloadFlush")]
    [InlineData("PayloadFlushed")]
    [InlineData("BlobPublished")]
    [InlineData("BeforePublicationFlush")]
    [InlineData("PublicationFlushed")]
    public void Failure_removes_only_owned_blob_and_releases_locks(string failAt)
    {
        using var box = new Sandbox();
        File.WriteAllText(box.Target, new string('s', 400000));
        var hash = BlobStore.Hash(box.Target)!;
        var observed = false;
        box.Store.Blobs.CaptureFault = (phase, _) =>
        {
            if (phase != failAt) return;
            observed = true;
            throw new IOException("injected capture failure");
        };
        Assert.Throws<IOException>(() => box.Store.Blobs.Capture(box.Target, hash));
        Assert.True(observed);
        Assert.Equal(hash, BlobStore.Hash(box.Target));
        Assert.False(File.Exists(box.Store.Blobs.PathFor(hash)));
        Assert.Empty(Directory.GetFiles(Path.Combine(box.Data, "blobs"), "*.pending"));
        using (File.Open(box.Target, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        box.Store.Blobs.CaptureFault = null;
        box.Store.Blobs.Capture(box.Target, hash);
        Assert.Equal(hash, BlobStore.Hash(box.Store.Blobs.PathFor(hash)));
    }

    [Theory]
    [InlineData("BeforePayloadFlush")]
    [InlineData("BeforePublicationFlush")]
    public void Failed_backup_barrier_prevents_any_original_replacement_or_receipt(string failAt)
    {
        using var box = new Sandbox();
        File.WriteAllText(box.Target, "original game bytes");
        var observed = false;
        box.Store.Blobs.CaptureFault = (phase, _) =>
        {
            if (phase != failAt) return;
            observed = true;
            throw new IOException("injected backup flush error");
        };
        var result = Assert.Single(box.Deploy());
        Assert.True(observed);
        Assert.Equal(OperationStatus.Restored, result.Status);
        Assert.Equal("original game bytes", File.ReadAllText(box.Target));
        Assert.Empty(box.Store.State.Receipts);
        Assert.Empty(box.Store.State.CommittedOperations);
        Assert.Empty(box.Store.Pending());
        Assert.Empty(Directory.GetFiles(box.Game.DeploymentRoot, ".rhi-*"));
    }

    [Fact]
    public void Readback_detects_written_corruption_not_just_input_hash()
    {
        using var box = new Sandbox();
        var hash = BlobStore.Hash(box.Source)!;
        box.Store.Blobs.CaptureFault = (phase, output) =>
        {
            if (phase != "BeforeReadback") return;
            output.Position = 0;
            output.WriteByte(0xFF);
            output.Flush(false);
        };
        Assert.Throws<DeploymentException>(() => box.Store.Blobs.Capture(box.Source, hash));
        Assert.Equal(hash, BlobStore.Hash(box.Source));
        Assert.False(File.Exists(box.Store.Blobs.PathFor(hash)));
        Assert.Empty(Directory.GetFiles(Path.Combine(box.Data, "blobs"), "*.pending"));
    }

    [Fact]
    public void Changed_input_does_not_publish_expected_hash()
    {
        using var box = new Sandbox();
        var hash = BlobStore.Hash(box.Source)!;
        File.WriteAllText(box.Source, "different approved bytes");
        Assert.Throws<DeploymentException>(() => box.Store.Blobs.Capture(box.Source, hash));
        Assert.False(File.Exists(box.Store.Blobs.PathFor(hash)));
        Assert.Empty(Directory.GetFiles(Path.Combine(box.Data, "blobs"), "*.pending"));
    }

    [Fact]
    public void Destination_arriving_before_rename_is_neither_overwritten_nor_deleted()
    {
        using var box = new Sandbox();
        var hash = BlobStore.Hash(box.Source)!;
        box.Store.Blobs.CaptureFault = (phase, _) =>
        {
            if (phase == "PayloadFlushed") File.WriteAllText(box.Store.Blobs.PathFor(hash), "competitor");
        };
        Assert.Throws<IOException>(() => box.Store.Blobs.Capture(box.Source, hash));
        Assert.Equal("competitor", File.ReadAllText(box.Store.Blobs.PathFor(hash)));
        Assert.Empty(Directory.GetFiles(Path.Combine(box.Data, "blobs"), "*.pending"));
    }

    [Fact]
    public void Source_cannot_be_modified_or_deleted_through_final_publication_barrier()
    {
        using var box = new Sandbox();
        var hash = BlobStore.Hash(box.Source)!;
        var observed = new List<string>();
        box.Store.Blobs.CaptureFault = (phase, _) =>
        {
            Assert.Throws<IOException>(() => File.WriteAllText(box.Source, "changed"));
            Assert.Throws<IOException>(() => File.Delete(box.Source));
            var ownedPath = Assert.Single(Directory.GetFiles(Path.Combine(box.Data, "blobs")));
            Assert.Throws<IOException>(() => File.WriteAllText(ownedPath, "changed backup"));
            Assert.Throws<IOException>(() => File.Delete(ownedPath));
            observed.Add(phase);
        };
        box.Store.Blobs.Capture(box.Source, hash);
        Assert.Contains("PublicationFlushed", observed);
        Assert.Equal(hash, BlobStore.Hash(box.Source));
        File.WriteAllText(box.Source, "released");
    }

    [Theory]
    [InlineData("PayloadFlushed")]
    [InlineData("BlobPublished")]
    public async Task Process_exit_before_return_leaves_original_and_allows_safe_retry(string phase)
    {
        // Process termination coverage, not a physical power-loss simulation.
        using var box = new Sandbox();
        File.WriteAllText(box.Target, new string('o', 400000));
        var hash = BlobStore.Hash(box.Target)!;
        box.Store.Dispose();
        var worker = Path.Combine(AppContext.BaseDirectory, "CrashWorker", "Rhi.CrashWorker.dll");
        Assert.True(File.Exists(worker));
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(worker);
        start.ArgumentList.Add(box.Data);
        start.ArgumentList.Add("Capture:" + phase);
        start.ArgumentList.Add(box.Target);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 97, $"Exit {process.ExitCode}: {await output} {await error}");
        using var reopened = WorkspaceStore.Open(box.Data);
        Assert.Equal(hash, BlobStore.Hash(box.Target));
        Assert.Empty(reopened.Pending());
        Assert.Empty(reopened.State.Receipts);
        reopened.Blobs.Capture(box.Target, hash);
        Assert.Equal(hash, BlobStore.Hash(reopened.Blobs.PathFor(hash)));
        // A pre-rename abrupt exit may retain an unreferenced .pending backup;
        // it is never adopted by name or confused with a committed blob.
    }

    [Fact]
    public void Optional_measurement_failure_does_not_change_capture_result()
    {
        using var box = new Sandbox();
        var hash = BlobStore.Hash(box.Source)!;
        box.Store.Blobs.CaptureObserver = _ => throw new InvalidOperationException("observer failure");
        box.Store.Blobs.Capture(box.Source, hash);
        Assert.Equal(hash, BlobStore.Hash(box.Store.Blobs.PathFor(hash)));
    }
}
