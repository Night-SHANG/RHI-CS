using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Deployment;

public sealed partial class DeploymentEngine
{
    public RecoveryPreview InspectRecovery(Guid operationId)
    {
        lock (gate)
        {
            var files = new List<RecoveryFilePreview>();
            var issues = new List<PlanIssue>();
            var committed = false;
            try
            {
                store.CheckAvailable();
                var journal = store.Pending().Single(j => j.Id == operationId);
                if (journal.WorkspaceId != store.State.Id) throw new DeploymentException("WrongDataSet");
                using var lease = DirectoryLease.Acquire(journal.TargetRoot);
                EnsureIdentity(journal);
                committed = store.State.CommittedOperations.Contains(journal.Id);
                if (committed)
                {
                    for (var i = 0; i <= journal.IntentIndex; i++) FileReplacement.ValidateCleanup(journal, i);
                }
                else ValidateRollback(journal);
                for (var i = 0; i < journal.Files.Length; i++)
                {
                    var file = journal.Files[i];
                    var current = BlobStore.Hash(PathPolicy.Target(journal.TargetRoot, file.RelativePath));
                    files.Add(new(file.RelativePath, current, file.BeforeHash,
                        !committed && i <= journal.IntentIndex && current != file.BeforeHash));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DeploymentException or InvalidOperationException)
            { issues.Add(new(ex is DeploymentException de ? de.Code : "InspectionFailed", ex.Message)); }
            return new(operationId, committed, files.ToArray(), issues.ToArray());
        }
    }

    private void ValidateRollback(OperationJournal journal)
    {
        for (var i = 0; i <= journal.IntentIndex; i++)
        {
            var file = journal.Files[i];
            if (file.BeforeHash is { } before && (journal.FileProtocol < 2 || FileReplacement.NeedsBackupBlob(journal, i)))
                store.Blobs.Verify(before);
            FileReplacement.ValidateRecovery(journal, i);
        }
    }
}
