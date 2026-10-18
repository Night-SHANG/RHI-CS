using Rhi.Core.Inspection;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.App.Services;

public sealed partial class AppSession
{
    public GameObservation? TargetObservation { get; private set; }
    public ProtectionInventory? ProtectionObservation { get; private set; }
    public bool ProtectionReadAttempted { get; private set; }

    public async Task<GameObservation> InspectTargets(Guid gameId, bool refresh = true)
    {
        RequireIdle(); TargetObservation = null;
        Busy = true; if (refresh) Changed?.Invoke();
        try { return TargetObservation = await Task.Run(() => DeploymentInspection.Inspect(Store!, gameId)); }
        finally { Busy = false; if (refresh) Changed?.Invoke(); }
    }

    public async Task InspectProtection()
    {
        RequireIdle(); ProtectionObservation = null; ProtectionReadAttempted = true;
        Busy = true;
        try { ProtectionObservation = await Task.Run(() => ProtectionCatalog.Read(Store!)); }
        finally { Busy = false; }
    }

    public void RecordSkipped(Guid gameId, DeploymentRequest[] skipped)
    {
        RequireIdle();
        TaskContext = Operations.OperationContext.Capture(State!, gameId, skipped);
        Results = skipped.Select(r => new OperationResult(r.LibraryId, OperationStatus.NotExecuted, "SkippedByUser")).ToArray();
        CurrentProgress = null;
        Changed?.Invoke();
    }
}
