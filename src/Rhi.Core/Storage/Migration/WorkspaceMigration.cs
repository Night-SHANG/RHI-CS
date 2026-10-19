using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Storage.Migration;

public static class WorkspaceMigration
{
    public static string Destination(WorkspaceStore source, string parent)
    {
        parent = PathPolicy.Full(parent);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        var destination = PathPolicy.Target(parent, "Data");
        MigrationFiles.CheckPaths(source, destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new DeploymentException("MigrationTargetExists", destination);
        return destination;
    }

    public static MigrationRecord Prepare(WorkspaceStore source, string destination, MigrationLog log)
    {
        if (log.Pending() is not null) throw new DeploymentException("MigrationPending");
        destination = PathPolicy.Full(destination);
        MigrationFiles.CheckPaths(source, destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new DeploymentException("MigrationTargetExists", destination);
        var inventory = MigrationFiles.Read(source.Root);
        source.CheckAvailable();
        var record = new MigrationRecord(Guid.NewGuid(), source.State.Id, source.Root, destination,
            DirectoryLease.Inspect(source.Root), null, inventory, MigrationStage.Copying);
        log.Write(record);
        return record;
    }

    // Caller owns the current workspace and reopens the authoritative locator after any outcome.
    // Checkpoints are fault-injection seams for remote tests, never selected by application input.
    public static void Run(WorkspaceStore current, MigrationLog log, Action<string, Guid> switchLocation,
        Action<string>? progress = null, Action<string>? checkpoint = null)
    {
        var record = log.Pending() ?? throw new DeploymentException("MigrationPending");
        if (current.State.Id != record.WorkspaceId) throw new DeploymentException("WrongDataSet");
        var atDestination = PathPolicy.Comparer.Equals(current.Root, record.Destination);
        if (!atDestination && !PathPolicy.Comparer.Equals(current.Root, record.Source)) throw new DeploymentException("DataLocationChanged");
        WorkspaceStore? openedDestination = null;
        try
        {
            if (!atDestination)
            {
                MigrationFiles.CheckPaths(current, record.Destination);
                if (DirectoryLease.Inspect(record.Source) != record.SourceIdentity) throw new DeploymentException("DataChanged", record.Source);
                MigrationFiles.Verify(record.Source, record.Inventory);
                progress?.Invoke("MigrationCopying");
                if (record.DestinationIdentity is null)
                {
                    if (File.Exists(record.Destination) || (Directory.Exists(record.Destination) && Directory.EnumerateFileSystemEntries(record.Destination).Any()))
                        throw new DeploymentException("MigrationTargetExists", record.Destination);
                    Directory.CreateDirectory(record.Destination);
                    record = record with { DestinationIdentity = DirectoryLease.Inspect(record.Destination) };
                    log.Write(record);
                }
                using (var targetLease = DirectoryLease.Acquire(record.Destination))
                {
                    if (targetLease.Identity != record.DestinationIdentity) throw new DeploymentException("DataChanged", record.Destination);
                    // A dedicated target never merges unrelated files. Partial copied files may be overwritten on retry.
                    foreach (var path in PathPolicy.Files(record.Destination))
                        if (!record.Inventory.Files.Any(f => PathPolicy.Comparer.Equals(f.Path, Path.GetRelativePath(record.Destination, path))) &&
                            !PathPolicy.Comparer.Equals(Path.GetRelativePath(record.Destination, path), "workspace.lock"))
                            throw new DeploymentException("MigrationContentChanged", path);
                    RobocopyTransfer.Copy(record.Source, record.Destination);
                    checkpoint?.Invoke("Copied");
                    progress?.Invoke("MigrationVerifying");
                    MigrationFiles.Verify(record.Source, record.Inventory);
                    MigrationFiles.Verify(record.Destination, record.Inventory);
                    current.CheckAvailable();
                    openedDestination = WorkspaceStore.Open(record.Destination, expectedId: record.WorkspaceId);
                    record = record with { Stage = MigrationStage.Verified };
                    log.Write(record);
                    checkpoint?.Invoke("Verified");
                    switchLocation(record.Destination, record.WorkspaceId);
                    checkpoint?.Invoke("Switched");
                }
                current.Dispose();
            }
            else if (record.Stage == MigrationStage.Copying)
                throw new DeploymentException("MigrationContentChanged", record.Destination);
            var destination = openedDestination ?? current;
            destination.CheckAvailable();
            if (DirectoryLease.Inspect(record.Destination) != record.DestinationIdentity) throw new DeploymentException("DataChanged", record.Destination);
            MigrationFiles.Verify(record.Destination, record.Inventory);
            record = record with { Stage = MigrationStage.Cleaning };
            log.Write(record);
            progress?.Invoke("MigrationCleaning");
            MigrationCleanup.Run(record, checkpoint);
            checkpoint?.Invoke("Cleaned");
            log.Write(record with { Stage = MigrationStage.Completed });
        }
        finally { openedDestination?.Dispose(); }
    }
}
