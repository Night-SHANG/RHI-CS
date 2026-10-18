namespace Rhi.Core.Models;

public sealed record RecoveryFilePreview(string RelativePath, string? CurrentHash, string? BeforeHash,
    bool RequiresRestore);
public sealed record RecoveryPreview(Guid OperationId, bool AlreadyCommitted, RecoveryFilePreview[] Files,
    PlanIssue[] Issues)
{
    public bool CanRecover => Issues.Length == 0;
}
