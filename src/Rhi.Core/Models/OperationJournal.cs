namespace Rhi.Core.Models;

public sealed record DirectoryIdentity(string Path, string FileId);

public sealed record JournalFile(string RelativePath, string? BeforeHash, string? DesiredHash,
    DirectoryIdentity? ParentIdentity = null);
public sealed record OperationJournal(Guid Id, Guid WorkspaceId, Guid GameId, Guid LibraryId,
    OperationKind Kind, string TargetRoot, string ExePath, string ExeHash, OperationStatus Status,
    JournalFile[] Files, string[] CreatedDirectories, int IntentIndex, string? Error,
    DateTimeOffset StartedAt, DirectoryIdentity? TargetIdentity = null, int FileProtocol = 0,
    string[]? PreviouslyCreatedDirectories = null);
public sealed record ProtectionEntry(Guid OperationId, Guid GameId, Guid LibraryId,
    string TargetRoot, JournalFile[] Files, DateTimeOffset CreatedAt,
    string? GameName = null, string? LibraryName = null, string? ProtectionDirectory = null,
    Guid? VersionId = null, string? VersionLabel = null, OperationKind? Kind = null,
    string[]? ModifiedFiles = null, bool FullVersion = false);
