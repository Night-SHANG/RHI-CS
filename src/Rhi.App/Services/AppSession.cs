using CommunityToolkit.Mvvm.ComponentModel;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.App.Services.Operations;

namespace Rhi.App.Services;

public sealed partial class AppSession : ObservableObject, IDisposable
{
    public WorkspaceStore? Store { get; private set; }
    public WorkspaceState? State => Store?.State;
    public DataPointer? Pointer { get; private set; }
    public string? StartupError { get; private set; }
    public bool InitializationBlocked { get; private set; }
    public IReadOnlyList<OperationResult> Results { get; private set; } = [];
    public OperationContext? TaskContext { get; private set; }
    private bool busy;
    public bool Busy { get => busy; private set => SetProperty(ref busy, value); }
    public bool IsWriting { get; private set; }
    public bool StopRequested { get; private set; }
    public TaskProgress? CurrentProgress { get; private set; }
    public bool Recovering => CurrentProgress?.Phase == "Recovering";
    private CancellationTokenSource? cancellation;
    public event Action? Changed;
    public event Action<TaskProgress>? Progress;

    public AppSession()
    {
        try { Pointer = Bootstrap.Read(); }
        catch (Exception ex) { StartupError = ex.Message; InitializationBlocked = true; return; }
        try
        {
            var store = StartupWorkspace.Open(AppContext.BaseDirectory, Pointer?.Root, Pointer?.Id);
            try
            {
                var pointer = new DataPointer(store.Root, store.State.Id);
                Bootstrap.Save(pointer);
                Store = store; Pointer = pointer;
            }
            catch { store.Dispose(); throw; }
        }
        catch (Exception ex) { StartupError = ex.Message; }
    }

    public void Connect(string path, bool create)
    {
        RequireIdle();
        if (Store is not null) throw new DeploymentException("MigrationNotSupported");
        if (InitializationBlocked) throw new DeploymentException("BootstrapInvalid");
        var store = WorkspaceStore.Open(path, create, Pointer?.Id);
        try
        {
            var pointer = new DataPointer(store.Root, store.State.Id);
            Bootstrap.Save(pointer);
            Store = store; Pointer = pointer; StartupError = null;
            TargetObservation = null; ProtectionObservation = null; ProtectionReadAttempted = false;
        }
        catch { store.Dispose(); throw; }
        Changed?.Invoke();
    }

    public void Save(WorkspaceState state)
    {
        RequireIdle();
        Store!.Save(state);
        TargetObservation = null;
        Changed?.Invoke();
    }

    public void InspectConnection()
    {
        if (Busy || Store is null) return;
        try { Store.CheckAvailable(); }
        catch (Exception ex)
        { StartupError = ex.Message; Store.Dispose(); Store = null; TargetObservation = null; ProtectionObservation = null; }
    }

    public async Task<BatchPlan> Preflight(Guid game, DeploymentRequest[] requests)
    {
        RequireIdle();
        Busy = true; Changed?.Invoke();
        try { return await Task.Run(() => new DeploymentPlanner(Store!).Build(State!, game, requests)); }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public async Task Execute(BatchPlan plan, OperationConsent consent, DeploymentRequest[]? skipped = null)
    {
        RequireIdle();
        skipped ??= [];
        TaskContext = OperationContext.Capture(State!, plan.GameId, plan.Libraries.Select(l => l.Request).Concat(skipped));
        TargetObservation = null; ProtectionObservation = null; ProtectionReadAttempted = false;
        Results = []; CurrentProgress = null;
        IsWriting = true; StopRequested = false; Busy = true; Changed?.Invoke();
        cancellation = new();
        var reporter = new Progress<TaskProgress>(p => { CurrentProgress = p; Progress?.Invoke(p); });
        try
        {
            Results = await Task.Run(() => new DeploymentEngine(Store!, gameSafetyCheck: CheckGame).Execute(plan, consent, reporter, cancellation.Token));
            Results = Results.Concat(skipped.Select(r => new OperationResult(r.LibraryId, OperationStatus.NotExecuted, "SkippedByUser"))).ToArray();
        }
        finally { cancellation.Dispose(); cancellation = null; IsWriting = false; Busy = false; Changed?.Invoke(); }
    }

    public async Task Recover(Guid id)
    {
        RequireIdle(); Results = []; CurrentProgress = new("Recovering", "", 0, 0);
        var journal = Store!.Pending().Single(j => j.Id == id);
        TaskContext = OperationContext.Capture(State!, journal.GameId, [new(journal.LibraryId, null, journal.Kind)], recovery: true);
        TargetObservation = null; ProtectionObservation = null; ProtectionReadAttempted = false;
        IsWriting = true; StopRequested = true; Busy = true; Changed?.Invoke();
        try { Results = [await Task.Run(() => new DeploymentEngine(Store!, gameSafetyCheck: CheckGame).Recover(id))]; }
        finally { IsWriting = false; Busy = false; Changed?.Invoke(); }
    }

    public async Task<RecoveryPreview> InspectRecovery(Guid id)
    {
        RequireIdle();
        Busy = true; Changed?.Invoke();
        try { return await Task.Run(() => new DeploymentEngine(Store!, gameSafetyCheck: CheckGame).InspectRecovery(id)); }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public void Stop() { if (Recovering) return; StopRequested = true; cancellation?.Cancel(); }
    private static void CheckGame(string exe)
    { if (WindowsIntegration.IsGameRunning(exe)) throw new DeploymentException("GameRunning"); }
    private void RequireIdle() { if (Busy) throw new DeploymentException("Busy"); }
    public void Dispose() { cancellation?.Dispose(); Store?.Dispose(); }
}
