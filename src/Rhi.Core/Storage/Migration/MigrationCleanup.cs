using System.Security.Cryptography;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Storage.Migration;

internal static class MigrationCleanup
{
    public static void Run(MigrationRecord record, Action<string>? checkpoint)
    {
        PathPolicy.CheckNoLinks(record.Source);
        if (!Directory.Exists(record.Source)) return;
        using (var lease = DirectoryLease.Acquire(record.Source))
        {
            if (lease.Identity != record.SourceIdentity) throw new DeploymentException("DataChanged", record.Source);
            using (var workspaceLock = new FileStream(Path.Combine(record.Source, "workspace.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None))
            {
                MigrationFiles.Verify(record.Source, record.Inventory, allowMissing: true);
                foreach (var item in record.Inventory.Files)
                {
                    var path = PathPolicy.Target(record.Source, item.Path);
                    if (!File.Exists(path)) continue;
                    var target = PathPolicy.Target(record.Destination, item.Path);
                    using var parent = DirectoryLease.Acquire(Path.GetDirectoryName(path)!);
                    using var targetParent = DirectoryLease.Acquire(Path.GetDirectoryName(target)!);
                    using var verifiedCopy = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (Convert.ToHexString(SHA256.HashData(verifiedCopy)) != item.Hash)
                        throw new DeploymentException("MigrationContentChanged", target);
                    using var original = NativeFile.Open(path);
                    NativeFile.Verify(original, item.Hash, path);
                    NativeFile.Delete(original);
                    checkpoint?.Invoke("FileRemoved");
                }
                // Never recursively delete. New or changed user files stop completion and remain intact.
                foreach (var relative in record.Inventory.Directories.OrderByDescending(d => d.Length))
                {
                    var directory = PathPolicy.Target(record.Source, relative);
                    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
                }
            }
            var lockPath = PathPolicy.Target(record.Source, "workspace.lock");
            using var lockFile = NativeFile.Open(lockPath);
            if (lockFile.Length != 0) throw new DeploymentException("MigrationContentChanged", lockPath);
            NativeFile.Delete(lockFile);
        }
        PathPolicy.CheckNoLinks(record.Source);
        if (DirectoryLease.Inspect(record.Source) != record.SourceIdentity) throw new DeploymentException("DataChanged", record.Source);
        Directory.Delete(record.Source, recursive: false);
    }
}
