using System.Buffers;
using System.Security.Cryptography;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Deployment;

// Same-volume staging + handle-based rename. The existing operation journal owns these paths.
internal static class FileReplacement
{
    private sealed record Locations(string Target, string Next, string Before, string Restore, string Displaced);

    private static Locations Paths(OperationJournal journal, int index, bool parentHeld = false)
    {
        if (journal.FileProtocol is not (1 or 2) || journal.TargetIdentity is null)
            throw new DeploymentException("RecoveryTargetChanged", journal.TargetRoot);
        var target = parentHeld
            ? Path.Combine(journal.TargetIdentity.Path, PathPolicy.Relative(journal.Files[index].RelativePath))
            : PathPolicy.Target(journal.TargetIdentity.Path, journal.Files[index].RelativePath);
        var prefix = Path.Combine(Path.GetDirectoryName(target)!, $".rhi-{journal.Id:N}-{index}");
        return new(target, prefix + ".next", prefix + ".before", prefix + ".restore", prefix + ".displaced");
    }

    private static void CheckParent(JournalFile file)
    {
        if (file.ParentIdentity is null || DirectoryLease.Inspect(file.ParentIdentity.Path) != file.ParentIdentity)
            throw new DeploymentException("GameIdentityChanged", file.RelativePath);
    }

    public static void Apply(BlobStore blobs, OperationJournal journal, int index, Action<string, int>? fault,
        ICollection<FileStream> parked, FileChange? source = null, bool parentHeld = false)
    {
        var paths = Paths(journal, index, parentHeld);
        var change = journal.Files[index];
        using var directory = parentHeld ? null : DirectoryLease.Acquire(Path.GetDirectoryName(paths.Target)!);
        if (!parentHeld) CheckParent(change);
        foreach (var path in new[] { paths.Next, paths.Before, paths.Restore, paths.Displaced })
            if (File.Exists(path) || Directory.Exists(path)) throw new DeploymentException("TargetConflict", path);
        if (journal.FileProtocol < 2 && BlobStore.Hash(paths.Target) != change.BeforeHash)
            throw new DeploymentException("ChangedSincePreflight", paths.Target);
        if (change.BeforeHash == change.DesiredHash)
        {
            // A no-op still adopts the preflight bytes into a receipt. Validate those
            // bytes under the same write-denying handle; no source copy is needed.
            if (change.BeforeHash is { } unchanged)
            {
                using var current = NativeFile.Open(paths.Target);
                NativeFile.Verify(current, unchanged, paths.Target);
            }
            else if (File.Exists(paths.Target) || Directory.Exists(paths.Target))
                throw new DeploymentException("ChangedSincePreflight", paths.Target);
            return;
        }

        using var staged = change.DesiredHash is { } desired
            ? journal.FileProtocol >= 2
                ? PrepareCopy(source?.SourcePath ?? (source?.GeneratedContent is null ? blobs.PathFor(desired) : null),
                    source?.GeneratedContent, desired, paths.Next, () => fault?.Invoke("CopyCompleted", index),
                    () => fault?.Invoke("StageWriting", index))
                : Prepare(blobs, desired, paths.Next, () => fault?.Invoke("StageWriting", index))
            : null;
        fault?.Invoke("StageReady", index);
        if (change.BeforeHash is { } before)
        {
            var original = NativeFile.Open(paths.Target);
            try
            {
                NativeFile.Verify(original, before, paths.Target);
                fault?.Invoke("TargetLocked", index);
                NativeFile.Rename(original, paths.Before);
                // The engine retains this exact identity through commit and deletion,
                // without reopening a mutable temporary path or hashing it again.
                parked.Add(original);
                original = null!;
            }
            finally { original?.Dispose(); }
        }
        else if (File.Exists(paths.Target) || Directory.Exists(paths.Target))
            throw new DeploymentException("ChangedSincePreflight", paths.Target);

        fault?.Invoke("TargetParked", index);
        if (staged is not null)
        {
            NativeFile.Rename(staged, paths.Target);
            // Buffered protocol-2 staging uses explicit durability boundaries rather
            // than WRITE_THROUGH on every copy buffer. Keep the handle through flush.
            if (journal.FileProtocol >= 2)
            {
                fault?.Invoke("PublishFlushing", index);
                staged.Flush(flushToDisk: true);
            }
        }
        fault?.Invoke("TargetPublished", index);
    }

    private static FileStream PrepareCopy(string? source, byte[]? content, string expected,
        string path, Action copied, Action duringWrite)
    {
        var output = NativeFile.Open(path, create: true, buffered: true);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (content is not null)
            {
                output.Write(content);
                hash.AppendData(content);
            }
            else
            {
                // One source read and one staging write, never a source cache or
                // a second staging read. Reject writers while copying these bytes.
                using var input = new FileStream(source!, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
                CopyBuffer.CopyAndHash(input, output, hash, duringWrite, ArrayPool<byte>.Shared);
            }
            if (Convert.ToHexString(hash.GetHashAndReset()) != expected)
                throw new DeploymentException("ChangedSincePreflight", source ?? path);
            // Drain managed writes before touching the target; the one durable
            // flush follows publication and must succeed before the receipt commits.
            output.Flush(flushToDisk: false);
            copied();
            return output;
        }
        catch
        {
            // Delete only our still-owned CREATE_NEW handle, never a reopened path.
            try { NativeFile.Delete(output); }
            finally { output.Dispose(); }
            throw;
        }
    }

    private static FileStream Prepare(BlobStore blobs, string hash, string path, Action? duringWrite = null)
    {
        blobs.Verify(hash);
        var output = NativeFile.Open(path, create: true);
        try
        {
            using var input = new FileStream(blobs.PathFor(hash), FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[81920];
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                output.Write(buffer, 0, count);
                duringWrite?.Invoke();
            }
            output.Flush(true);
            NativeFile.Verify(output, hash, path);
            return output;
        }
        catch
        {
            // This exact handle was created by us and has never been shared for writing/deletion.
            try { NativeFile.Delete(output); }
            finally { output.Dispose(); }
            throw;
        }
    }

    public static void ValidateRecovery(OperationJournal journal, int index)
    {
        var paths = Paths(journal, index);
        using var directory = DirectoryLease.Acquire(Path.GetDirectoryName(paths.Target)!);
        var file = journal.Files[index];
        CheckParent(file);
        VerifyOptional(paths.Before, file.BeforeHash);
        if (journal.FileProtocol < 2) VerifyOptional(paths.Next, file.DesiredHash);
        VerifyOptional(paths.Restore, file.BeforeHash);
        VerifyOptional(paths.Displaced, file.DesiredHash);
        var current = BlobStore.Hash(paths.Target);
        if (current == file.BeforeHash || current == file.DesiredHash) return;
        // A missing destination is a known rename boundary only with its verified parked file.
        if (current is null && (File.Exists(paths.Before) || File.Exists(paths.Displaced))) return;
        throw new DeploymentException("RecoveryTargetChanged", file.RelativePath);
    }

    public static void Restore(BlobStore blobs, OperationJournal journal, int index)
    {
        ValidateRecovery(journal, index);
        var paths = Paths(journal, index);
        using var directory = DirectoryLease.Acquire(Path.GetDirectoryName(paths.Target)!);
        var file = journal.Files[index];
        var current = BlobStore.Hash(paths.Target);
        CheckParent(file);
        if (current == file.BeforeHash) return;
        if (current is not null)
        {
            using var installed = NativeFile.Open(paths.Target);
            NativeFile.Verify(installed, file.DesiredHash!, paths.Target);
            NativeFile.Rename(installed, paths.Displaced);
        }
        if (file.BeforeHash is { } before)
        {
            var restorePath = File.Exists(paths.Before) ? paths.Before : paths.Restore;
            using var original = File.Exists(restorePath) ? NativeFile.Open(restorePath) : Prepare(blobs, before, restorePath);
            NativeFile.Verify(original, before, restorePath);
            NativeFile.Rename(original, paths.Target);
        }
    }

    public static void Cleanup(OperationJournal journal, int index, bool verify = true, bool parentHeld = false)
    {
        var paths = Paths(journal, index, parentHeld);
        using var directory = parentHeld ? null : DirectoryLease.Acquire(Path.GetDirectoryName(paths.Target)!);
        var file = journal.Files[index];
        if (!parentHeld) CheckParent(file);
        RemoveVerified(paths.Before, file.BeforeHash, verify);
        RemoveVerified(paths.Next, file.DesiredHash, verify && journal.FileProtocol < 2);
        RemoveVerified(paths.Restore, file.BeforeHash, verify);
        RemoveVerified(paths.Displaced, file.DesiredHash, verify);
    }

    public static bool NeedsBackupBlob(OperationJournal journal, int index)
    {
        var paths = Paths(journal, index);
        var file = journal.Files[index];
        return file.BeforeHash is not null && BlobStore.Hash(paths.Target) != file.BeforeHash &&
            !File.Exists(paths.Before) && !File.Exists(paths.Restore);
    }

    public static void ValidateCleanup(OperationJournal journal, int index)
    {
        var paths = Paths(journal, index);
        using var directory = DirectoryLease.Acquire(Path.GetDirectoryName(paths.Target)!);
        var file = journal.Files[index];
        CheckParent(file);
        VerifyOptional(paths.Before, file.BeforeHash);
        VerifyOptional(paths.Next, file.DesiredHash);
        VerifyOptional(paths.Restore, file.BeforeHash);
        VerifyOptional(paths.Displaced, file.DesiredHash);
    }

    private static void VerifyOptional(string path, string? expected)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if (expected is null || BlobStore.Hash(path) != expected)
            throw new DeploymentException("RecoveryTargetChanged", path);
    }

    private static void RemoveVerified(string path, string? expected, bool verify = true)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if (expected is null) throw new DeploymentException("RecoveryTargetChanged", path);
        using var file = NativeFile.Open(path);
        if (verify) NativeFile.Verify(file, expected, path);
        NativeFile.Delete(file);
    }
}
