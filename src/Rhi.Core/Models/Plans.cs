using System.Text.Json.Serialization;

namespace Rhi.Core.Models;

public sealed record DeploymentRequest(Guid LibraryId, Guid? VersionId, OperationKind Kind);
public sealed record PlanIssue(string Code, string Detail);
public sealed record SourceSnapshot(string Path, string? Hash);
public sealed record ConfigurationEdit(string Section, string Key, string? Before, string After);
public sealed record FileChange(string RelativePath, string? SourcePath, string? DesiredHash,
    string? BeforeHash, string? OriginalHash, bool IsManagedAfter, bool ExternalChange,
    DirectoryIdentity? ParentIdentity = null,
    [property: JsonIgnore] byte[]? GeneratedContent = null, ConfigurationEdit[]? ConfigurationEdits = null);
public sealed record LibraryPlan(DeploymentRequest Request, string LibraryName, string VersionLabel,
    FileChange[] Changes, string[] PreviouslyCreatedDirectories, bool RequiresOverwrite, bool RequiresProtection,
    SourceSnapshot[]? Inputs = null);
public sealed record BatchPlan(Guid WorkspaceId, long Revision, Guid GameId, string TargetRoot,
    string ExePath, string ExeHash, LibraryPlan[] Libraries, PlanIssue[] Issues,
    DirectoryIdentity? TargetIdentity = null)
{
    public bool CanExecute => Issues.Length == 0 && Libraries.Length > 0;
}
public sealed record OperationConsent(bool BackupOriginals, bool ProtectChanges, bool DiscardChangesOnClear = false);
public sealed record OperationResult(Guid LibraryId, OperationStatus Status, string Detail, Guid? OperationId = null);
public sealed record TaskProgress(string Phase, string Library, int Completed, int Total);
public sealed class DeploymentException(string code, string? detail = null) : Exception(detail is null ? code : $"{code}: {detail}")
{
    public string Code { get; } = code;
}
