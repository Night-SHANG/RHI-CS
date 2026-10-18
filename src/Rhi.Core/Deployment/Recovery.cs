using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Deployment;

public sealed partial class DeploymentEngine
{
    public OperationResult Recover(Guid operationId)
    {
        lock (gate)
        {
            var journal = store.Pending().Single(j => j.Id == operationId);
            if (journal.WorkspaceId != store.State.Id) throw new DeploymentException("WrongDataSet");
            if (store.State.CommittedOperations.Contains(journal.Id))
            {
                using var lease = DirectoryLease.Acquire(journal.TargetRoot);
                EnsureIdentity(journal);
                for (var i = 0; i <= journal.IntentIndex; i++) FileReplacement.Cleanup(journal, i);
                FileWriter.CleanEmptyDirectories(lease.Identity.Path,
                    journal.CreatedDirectories.Union(journal.PreviouslyCreatedDirectories ?? []));
                store.WriteJournal(journal with { Status = OperationStatus.Succeeded });
                return new(journal.LibraryId, OperationStatus.Succeeded, "Succeeded", journal.Id);
            }
            return Rollback(journal, false, "Interrupted");
        }
    }

    private OperationResult Rollback(OperationJournal journal, bool cancelled, string reason)
    {
        try
        {
            store.CheckAvailable();
            using var lease = DirectoryLease.Acquire(journal.TargetRoot);
            EnsureIdentity(journal);
            // Inspect the whole rollback first. A third-party change after a crash must not be overwritten.
            ValidateRollback(journal);
            for (var i = journal.IntentIndex; i >= 0; i--)
            {
                FileReplacement.Restore(store.Blobs, journal, i);
            }
            for (var i = 0; i <= journal.IntentIndex; i++)
                if (BlobStore.Hash(PathPolicy.Target(journal.TargetRoot, journal.Files[i].RelativePath)) != journal.Files[i].BeforeHash)
                    throw new DeploymentException("VerificationFailed", journal.Files[i].RelativePath);
            for (var i = 0; i <= journal.IntentIndex; i++) FileReplacement.Cleanup(journal, i);
            FileWriter.CleanEmptyDirectories(lease.Identity.Path, journal.CreatedDirectories);
            var status = cancelled && journal.IntentIndex < 0 ? OperationStatus.Cancelled : OperationStatus.Restored;
            var detail = cancelled ? (journal.IntentIndex < 0 ? "Cancelled" : "StoppedRestored") : "FailedRestored";
            store.WriteJournal(journal with { Status = status, Error = reason });
            return new(journal.LibraryId, status, detail, journal.Id);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            try { store.WriteJournal(journal with { Status = OperationStatus.NeedsRepair, Error = ex.Message }); }
            catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException or DeploymentException) { }
            return new(journal.LibraryId, OperationStatus.NeedsRepair, ex.Message, journal.Id);
        }
    }
}
