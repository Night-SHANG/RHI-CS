using Rhi.Core.Models;
using Rhi.Core.Deployment;

namespace Rhi.Core.Storage.Migration;

public enum MigrationStage { Copying, Verified, Cleaning, Completed }
public sealed record MigrationFile(string Path, string Hash);
public sealed record MigrationInventory(MigrationFile[] Files, string[] Directories);
public sealed record MigrationRecord(Guid Id, Guid WorkspaceId, string Source, string Destination,
    DirectoryIdentity SourceIdentity, DirectoryIdentity? DestinationIdentity,
    MigrationInventory Inventory, MigrationStage Stage);

public sealed class MigrationLog(string path)
{
    public MigrationRecord? Read()
    {
        PathPolicy.CheckNoLinks(path);
        return File.Exists(path) ? AtomicJson.Read<MigrationRecord>(path) : null;
    }
    public MigrationRecord? Pending() => Read() is { Stage: not MigrationStage.Completed } record ? record : null;
    public void Write(MigrationRecord record)
    {
        PathPolicy.CheckNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicJson.Write(path, record);
    }
}
