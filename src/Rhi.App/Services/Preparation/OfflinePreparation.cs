using Rhi.Core.Preparation;

namespace Rhi.App.Services;

public sealed partial class AppSession
{
    public async Task<string> CreateOfflineReShade(OfflineReShadeRequest request)
    {
        RequireIdle();
        Store!.CheckAvailable();
        Busy = true;
        SavingConfiguration = true;
        Changed?.Invoke();
        try { return await Task.Run(() => OfflineReShade.Create(State!, Store.Root, request)); }
        finally { SavingConfiguration = false; Busy = false; Changed?.Invoke(); }
    }
}
