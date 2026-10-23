using Rhi.App.Services.Operations;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.App.Services;

// Test-only shell for the linked production AppSession.Observations.cs methods.
// Avoids starting WinUI/Bootstrap; inspection and storage below are not mocked.
public sealed partial class AppSession(WorkspaceStore store)
{
    public WorkspaceStore? Store { get; } = store;
    public WorkspaceState? State => Store?.State;
    public bool Busy { get; private set; }
    public event Action? Changed;
    public OperationContext? TaskContext { get; private set; }
    public IReadOnlyList<OperationResult> Results { get; private set; } = [];
    public TaskProgress? CurrentProgress { get; private set; }
    private void RequireIdle()
    {
        if (Busy) throw new DeploymentException("Busy");
    }
}
