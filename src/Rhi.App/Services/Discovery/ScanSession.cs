using Rhi.Core.Discovery;
using Rhi.App.Services.Discovery;

namespace Rhi.App.Services;

public sealed partial class AppSession
{
    public async Task<DiscoveryResult> ScanGames()
    {
        RequireIdle(); Busy = true; Changed?.Invoke();
        try { return await Task.Run(RhiGameDiscovery.Scan); }
        finally { Busy = false; Changed?.Invoke(); }
    }
}
