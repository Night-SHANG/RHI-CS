namespace Rhi.Core.Models;

public enum ResourceKind { Files, ReShadeStandard, ReShadeAddon }
public enum OperationKind { Deploy, Change, Clear }
public enum OperationStatus { Preparing, Executing, Succeeded, Cancelled, Restored, NeedsRepair, NotExecuted }

public sealed record GameEntry(Guid Id, string Name, string InstallRoot, string ExePath,
    string DeploymentRoot, string Api, string Architecture, string Source);
public sealed record ResourceVersion(Guid Id, string Label, string SourcePath,
    string? TargetOverride, ResourceKind Kind);
public sealed record ResourceLibrary(Guid Id, string Name, string TargetRelative,
    ResourceVersion[] Versions, string? SourceRoot = null);
public sealed record ManagedFile(string RelativePath, string InstalledHash, string? OriginalHash);
public sealed record DeploymentReceipt(Guid GameId, Guid LibraryId, Guid VersionId,
    string VersionLabel, string TargetRoot, string ExeHash, ManagedFile[] Files, string[] CreatedDirectories,
    DirectoryIdentity? TargetIdentity = null);
public sealed record Preferences(string Theme = "Light", string Language = "System", string? LibraryRoot = null);
public sealed record WorkspaceState(Guid Id, long Revision, GameEntry[] Games,
    ResourceLibrary[] Libraries, DeploymentReceipt[] Receipts, Guid[] CommittedOperations,
    Preferences Preferences)
{
    public static WorkspaceState New() => new(Guid.NewGuid(), 0, [], [], [], [], new());
}
