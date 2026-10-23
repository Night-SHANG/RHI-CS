using Rhi.Core.Storage;

namespace Rhi.App.Services;

public sealed partial class AppSession
{
    public bool RemoveProtectionRecord(Guid operationId, bool confirmed)
    {
        RequireIdle();
        if (!ProtectionCatalog.RemoveRecord(Store!, operationId, confirmed)) return false;
        if (ProtectionObservation is { } inventory)
            ProtectionObservation = inventory with
            {
                Operations = inventory.Operations.Where(copy => copy.Entry.OperationId != operationId).ToArray()
            };
        Changed?.Invoke();
        return true;
    }
}
