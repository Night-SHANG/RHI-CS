using Rhi.Core.Configuration;

namespace Rhi.App.Services;

public sealed partial class AppSession
{
    public bool SavingConfiguration { get; private set; }
    public async Task<RelocationPlan> InspectRelocation(Guid game, string exe, string install, string target)
    {
        RequireIdle(); Busy = true; Changed?.Invoke();
        try { CheckGame(exe); return await Task.Run(() => GameRelocation.Inspect(Store!, game, exe, install, target)); }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public async Task ApplyRelocation(RelocationPlan plan)
    {
        RequireIdle(); Busy = true; SavingConfiguration = true; Changed?.Invoke();
        try { await Task.Run(() => GameRelocation.Apply(Store!, plan, CheckGame)); TargetObservation = null; }
        finally { SavingConfiguration = false; Busy = false; Changed?.Invoke(); }
    }
}
