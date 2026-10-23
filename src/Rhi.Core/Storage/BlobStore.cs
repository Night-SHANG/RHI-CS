using System.Security.Cryptography;
using System.Buffers;
using System.Diagnostics;
using Rhi.Core.Storage.Windows;
using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Storage;

public sealed class BlobStore(string root)
{
    public Action<BlobCaptureMeasurement>? CaptureObserver { get; set; }
    internal Action<string, FileStream>? CaptureFault { get; set; }

    public string PathFor(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid blob id");
        var path = Path.Combine(root, "blobs", hash);
        PathPolicy.CheckNoLinks(path);
        return path;
    }

    public static string? Hash(string path)
    {
        PathPolicy.CheckNoLinks(path);
        if (Directory.Exists(path)) throw new DeploymentException("FileDirectoryConflict", path);
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public void Verify(string hash)
    {
        if (Hash(PathFor(hash)) != hash) throw new DeploymentException("BackupUnavailable", hash);
    }

    public void CaptureGenerated(byte[] content, string expectedHash)
    {
        if (Convert.ToHexString(SHA256.HashData(content)) != expectedHash)
            throw new DeploymentException("ChangedSincePreflight");
        var dest = PathFor(expectedHash);
        if (File.Exists(dest)) { Verify(expectedHash); return; }
        var temp = dest + "." + Guid.NewGuid().ToString("N") + ".pending";
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.WriteThrough))
        {
            output.Write(content);
            output.Flush(true);
        }
        if (Hash(temp) != expectedHash) throw new DeploymentException("ChangedSincePreflight");
        File.Move(temp, dest);
        Verify(expectedHash);
    }

    public void Capture(string source, string expectedHash)
    {
        var observer = CaptureObserver;
        var watch = observer is null ? null : Stopwatch.StartNew();
        long sourceBytesRead = 0, backupBytesRead = 0, bytesWritten = 0, bytesHashed = 0;
        int hashPasses = 0, flushCalls = 0, publishedBlobs = 0, reusedBlobs = 0;
        double copySeconds = 0, verificationSeconds = 0, durableFlushSeconds = 0, publishSeconds = 0;
        var succeeded = false;
        try
        {
            var dest = PathFor(expectedHash);
            if (File.Exists(dest))
            {
                var phase = observer is null ? null : Stopwatch.StartNew();
                try
                {
                    // Existing immutable blobs are never repaired or overwritten here.
                    using var existing = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.Read);
                    hashPasses++;
                    var actual = Convert.ToHexString(SHA256.HashData(existing));
                    backupBytesRead += existing.Position;
                    bytesHashed += existing.Position;
                    if (actual != expectedHash) throw new DeploymentException("BackupUnavailable", expectedHash);
                }
                finally { if (phase is not null) verificationSeconds += phase.Elapsed.TotalSeconds; }
                reusedBlobs++;
                succeeded = true;
                return;
            }
            var temp = dest + "." + Guid.NewGuid().ToString("N") + ".pending";
            PathPolicy.CheckNoLinks(source);
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = NativeFile.Open(temp, create: true, buffered: true);
            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var phase = observer is null ? null : Stopwatch.StartNew();
                hashPasses++;
                try
                {
                    CopyBuffer.CopyAndHash(input, output, hash,
                        () => CaptureFault?.Invoke("CopyWriting", output), ArrayPool<byte>.Shared);
                    if (Convert.ToHexString(hash.GetHashAndReset()) != expectedHash)
                        throw new DeploymentException("ChangedSincePreflight", source);
                }
                finally
                {
                    sourceBytesRead += input.Position;
                    bytesWritten += output.Position;
                    bytesHashed += input.Position;
                    if (phase is not null) copySeconds += phase.Elapsed.TotalSeconds;
                }
                output.Flush(false);
                CaptureFault?.Invoke("BeforeReadback", output);
                phase = observer is null ? null : Stopwatch.StartNew();
                output.Position = 0;
                hashPasses++;
                try
                {
                    if (Convert.ToHexString(SHA256.HashData(output)) != expectedHash)
                        throw new DeploymentException("ChangedSincePreflight", source);
                }
                finally
                {
                    backupBytesRead += output.Position;
                    bytesHashed += output.Position;
                    if (phase is not null) verificationSeconds += phase.Elapsed.TotalSeconds;
                }
                // Persist payload before making a reusable blob name visible. A later
                // process may take the cache-hit path without another durability barrier.
                DurableFlush("BeforePayloadFlush", output);
                CaptureFault?.Invoke("PayloadFlushed", output);
                phase = observer is null ? null : Stopwatch.StartNew();
                try { NativeFile.Rename(output, dest); publishedBlobs++; }
                finally { if (phase is not null) publishSeconds += phase.Elapsed.TotalSeconds; }
                CaptureFault?.Invoke("BlobPublished", output);
                DurableFlush("BeforePublicationFlush", output);
                CaptureFault?.Invoke("PublicationFlushed", output);
                succeeded = true;
            }
            catch (Exception error)
            {
                // Only this newly created identity is ours, including after rename.
                // A competing destination is never deleted or overwritten.
                try { NativeFile.Delete(output); }
                catch (Exception cleanupError)
                {
                    throw new AggregateException("Backup capture failed and its owned file could not be removed.", error, cleanupError);
                }
                throw;
            }
        }
        finally
        {
            if (observer is not null)
            {
                watch!.Stop();
                try
                {
                    observer(new(watch.Elapsed.TotalSeconds, sourceBytesRead, backupBytesRead, bytesWritten,
                        bytesHashed, hashPasses, flushCalls, publishedBlobs, reusedBlobs, succeeded, false,
                        copySeconds, verificationSeconds, durableFlushSeconds, publishSeconds));
                }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                {
                    // Optional diagnostics cannot change the deployment outcome.
                }
            }
        }

        void DurableFlush(string phaseName, FileStream output)
        {
            CaptureFault?.Invoke(phaseName, output);
            var phase = observer is null ? null : Stopwatch.StartNew();
            flushCalls++;
            try { output.Flush(true); }
            finally { if (phase is not null) durableFlushSeconds += phase.Elapsed.TotalSeconds; }
        }
    }
}
