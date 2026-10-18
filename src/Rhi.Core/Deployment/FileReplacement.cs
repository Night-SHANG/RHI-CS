using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Deployment;

// Same-volume staging + handle-based rename. The existing operation journal owns these paths.
internal static class FileReplacement
{
    private sealed record Locations(string Target, string Next, string Before, string Restore, string Displaced);

    private static Locations Paths(OperationJournal journal, int index)
    {
        if (journal.FileProtocol != 1 || journal.TargetIdentity is null)
            throw new DeploymentException("RecoveryTargetChanged", journal.TargetRoot);
        var target = PathPolicy.Target(journal.TargetIdentity.Path, journal.Files[index].RelativePath);
        var prefix = Path.Combine(Path.GetDirectoryName(target)!, $".rhi-{journal.Id:N}-{index}");
        return new(target, prefix + ".next", prefix + ".before", prefix + ".restore", prefix + ".displaced");
    }

    private static void CheckParent(JournalFile file)
    {
        if (file.ParentIdentity is null || DirectoryLease.Inspect(file.ParentIdentity.Path) != file.ParentIdentity)
            throw new DeploymentException("GameIdentityChanged", file.RelativePath);
    }

    public static void Apply(BlobStore blobs, OperationJournal journal, int index, Action<string, int>? fault)
    {
        var paths = Paths(journal, index);
        var change = journal.Files[index];
        using var directory = DirectoryLease.Acquire(Path.GetDirectoryName(paths.Target)!);
        CheckParent(change);
        foreach (var path in new[] { paths.Next, paths.Before, paths.Restore, paths.Displaced })
            if (File.Exists(path) || Directory.Exists(path)) throw new DeploymentException("TargetConflict", path);
        if (BlobStore.Hash(paths.Target) != change.BeforeHash)
            throw new DeploymentException("ChangedSincePreflight", paths.Target);

        using var staged = change.DesiredHash is { } desired
            ? Prepare(blobs, desired, paths.Next, () => fault?.Invoke("StageWriting", index)) : null;
        fault?.Invoke("StageReady", index);
        if (change.BeforeHash is { } before)
        {
            using var original = NativeFile.Open(paths.Target);
            NativeFile.Verify(original, before, paths.Target);
            fault?.Invoke("TargetLocked", index);
            NativeFile.Rename(original, paths.Before);
        }
        else if (File.Exists(paths.Target) || Directory.Exists(paths.Target))
            throw new DeploymentException("ChangedSincePreflight", paths.Target);

        fault?.Invoke("TargetParked", index);
        if (staged is not null) NativeFile.Rename(staged, paths.Target);
        fault?.Invoke("TargetPublished", index);
        // Verification is performed after all held streams have closed by the executor.
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
        VerifyOptional(paths.Next, file.DesiredHash);
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

    public static void Cleanup(OperationJournal journal, int index)
    {
        var paths = Paths(journal, index);
        using var directory = DirectoryLease.Acquire(Path.GetDirectoryName(paths.Target)!);
        var file = journal.Files[index];
        CheckParent(file);
        RemoveVerified(paths.Before, file.BeforeHash);
        RemoveVerified(paths.Next, file.DesiredHash);
        RemoveVerified(paths.Restore, file.BeforeHash);
        RemoveVerified(paths.Displaced, file.DesiredHash);
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

    private static void RemoveVerified(string path, string? expected)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if (expected is null) throw new DeploymentException("RecoveryTargetChanged", path);
        using var file = NativeFile.Open(path);
        NativeFile.Verify(file, expected, path);
        NativeFile.Delete(file);
    }
}
