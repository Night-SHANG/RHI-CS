using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Inspection;

public static class DeploymentInspection
{
    public static GameObservation Inspect(WorkspaceStore store, Guid gameId)
    {
        store.CheckAvailable();
        var state = store.State;
        var game = state.Games.Single(g => g.Id == gameId);
        var observations = state.Receipts.Where(r => r.GameId == gameId).Select(receipt => Inspect(game, receipt)).ToArray();
        store.CheckAvailable();
        return new(gameId, state.Revision, DateTimeOffset.UtcNow, observations);
    }

    private static ReceiptObservation Inspect(GameEntry game, DeploymentReceipt receipt)
    {
        var files = new List<TargetFileObservation>();
        var issues = new List<PlanIssue>();
        try
        {
            if (!Directory.Exists(receipt.TargetRoot) || !File.Exists(game.ExePath))
                throw new DeploymentException("GameUnavailable", receipt.TargetRoot);
            using var lease = DirectoryLease.Acquire(receipt.TargetRoot);
            if (receipt.TargetIdentity != lease.Identity || !PathPolicy.Comparer.Equals(receipt.TargetRoot, game.DeploymentRoot))
                throw new DeploymentException("GameIdentityChanged", receipt.TargetRoot);
            foreach (var file in receipt.Files)
            {
                var target = PathPolicy.Target(receipt.TargetRoot, file.RelativePath);
                FileIdentity.AssertSingleLink(target);
                var hash = BlobStore.Hash(target);
                files.Add(new(file.RelativePath, hash is null ? TargetHealth.TargetMissing :
                    hash == file.InstalledHash ? TargetHealth.TargetMatched : TargetHealth.TargetModified));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DeploymentException)
        { issues.Add(new(ex is DeploymentException de ? de.Code : "InspectionFailed", ex.Message)); }
        var health = issues.Count > 0 ? TargetHealth.TargetUnavailable :
            files.Any(f => f.Health == TargetHealth.TargetModified) ? TargetHealth.TargetModified :
            files.Any(f => f.Health == TargetHealth.TargetMissing) ? TargetHealth.TargetMissing : TargetHealth.TargetMatched;
        return new(receipt.LibraryId, health, files.ToArray(), issues.ToArray());
    }
}
