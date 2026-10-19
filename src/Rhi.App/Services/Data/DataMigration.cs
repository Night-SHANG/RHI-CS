using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Migration;

namespace Rhi.App.Services;

public sealed partial class AppSession
{
    public MigrationRecord? PendingMigration { get; private set; }
    public string? MigrationPhase { get; private set; }

    public async Task MigrateData(string? destination = null)
    {
        if (Busy) throw new DeploymentException("Busy");
        var current = Store ?? throw new DeploymentException("DataUnavailable");
        var expected = Pointer;
        Busy = true; SavingConfiguration = true; MigrationPhase = "MigrationVerifying";
        Changed?.Invoke();
        var reporter = new Progress<string>(phase =>
        {
            if (!SavingConfiguration || MigrationPhase is null) return;
            MigrationPhase = phase; Changed?.Invoke();
        });
        try
        {
            await Task.Run(() =>
            {
                if (Bootstrap.Read() != expected) throw new DeploymentException("DataLocationChanged");
                var log = Bootstrap.Migration;
                if (log.Pending() is null)
                    WorkspaceMigration.Prepare(current, destination ?? throw new DeploymentException("MigrationPending"), log);
                WorkspaceMigration.Run(current, log, (root, id) =>
                {
                    if (Bootstrap.Read() != expected) throw new DeploymentException("DataLocationChanged");
                    Bootstrap.Save(new(root, id));
                }, phase => ((IProgress<string>)reporter).Report(phase));
            });
        }
        finally
        {
            current.Dispose(); Store = null;
            TargetObservation = null; ProtectionObservation = null; ProtectionReadAttempted = false;
            try
            {
                PendingMigration = Bootstrap.Migration.Pending();
                Pointer = Bootstrap.Read() ?? throw new DeploymentException("DataLocationChanged");
                Store = WorkspaceStore.Open(Pointer.Root, expectedId: Pointer.Id);
                StartupError = null;
            }
            catch (Exception ex) { StartupError = ex.Message; }
            MigrationPhase = null; SavingConfiguration = false; Busy = false;
            Changed?.Invoke();
        }
    }
}
