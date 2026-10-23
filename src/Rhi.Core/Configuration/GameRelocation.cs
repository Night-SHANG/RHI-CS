using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Configuration;

public sealed record RelocationPlan(Guid WorkspaceId, long Revision, GameEntry Original, GameEntry Candidate,
    DirectoryIdentity? Identity, int Libraries, int Files, PlanIssue[] Issues)
{
    public bool CanApply => Issues.Length == 0 && Identity is not null && Libraries > 0;
}

public static class GameRelocation
{
    public static RelocationPlan Inspect(WorkspaceStore store, Guid gameId, string exe, string install, string target)
    {
        using var held = new ReadLocks();
        return Inspect(store, gameId, exe, install, target, held);
    }

    public static void Apply(WorkspaceStore store, RelocationPlan approved, Action<string>? gameSafetyCheck = null)
    {
        store.CheckAvailable();
        if (!approved.CanApply || approved.WorkspaceId != store.State.Id || approved.Revision != store.State.Revision)
            throw new DeploymentException("ChangedSincePreflight");
        using var held = new ReadLocks();
        var actual = Inspect(store, approved.Original.Id, approved.Candidate.ExePath,
            approved.Candidate.InstallRoot, approved.Candidate.DeploymentRoot, held);
        if (!actual.CanApply || AtomicJson.Fingerprint(actual) != AtomicJson.Fingerprint(approved))
            throw new DeploymentException("ChangedSincePreflight");
        gameSafetyCheck?.Invoke(actual.Candidate.ExePath);
        AssertOldLocationMissing(actual.Original.DeploymentRoot);
        // One atomic ledger replacement. No game/source writes and no separate relocation journal.
        var state = store.State;
        store.Save(state with
        {
            Games = state.Games.Select(g => g.Id == actual.Original.Id ? actual.Candidate : g).ToArray(),
            Receipts = state.Receipts.Select(r => r.GameId != actual.Original.Id ? r : r with
            {
                TargetRoot = actual.Candidate.DeploymentRoot, TargetIdentity = actual.Identity,
                // A copied directory has no proven ownership of its empty subdirectories.
                CreatedDirectories = r.TargetIdentity?.FileId == actual.Identity!.FileId ? r.CreatedDirectories : []
            }).ToArray()
        });
    }

    private static RelocationPlan Inspect(WorkspaceStore store, Guid gameId, string exe, string install, string target, ReadLocks held)
    {
        store.CheckAvailable();
        var state = store.State;
        var old = state.Games.Single(g => g.Id == gameId);
        var candidate = old with { ExePath = exe, InstallRoot = install, DeploymentRoot = target };
        var receipts = state.Receipts.Where(r => r.GameId == gameId).ToArray();
        DirectoryIdentity? identity = null;
        var issues = new List<PlanIssue>();
        try
        {
            if (store.Pending().Any(j => j.GameId == gameId)) throw new DeploymentException("PendingConfigurationLocked");
            if (receipts.Length == 0) throw new DeploymentException("RelocationNoDeployment");
            AssertOldLocationMissing(old.DeploymentRoot);
            candidate = ConfigurationRules.Game(state, store.Root, candidate, relocation: true);
            if (!PathPolicy.Comparer.Equals(Path.GetRelativePath(old.InstallRoot, old.ExePath), Path.GetRelativePath(candidate.InstallRoot, candidate.ExePath)) ||
                !PathPolicy.Comparer.Equals(Path.GetRelativePath(old.InstallRoot, old.DeploymentRoot), Path.GetRelativePath(candidate.InstallRoot, candidate.DeploymentRoot)))
                throw new DeploymentException("RelocationLayoutChanged");
            identity = held.Directory(candidate.DeploymentRoot);
            held.File(candidate.ExePath);
            var paths = new HashSet<string>(PathPolicy.Comparer);
            foreach (var receipt in receipts)
            {
                if (!PathPolicy.Comparer.Equals(receipt.TargetRoot, old.DeploymentRoot) || receipt.TargetIdentity is null)
                    throw new DeploymentException("GameIdentityChanged", candidate.ExePath);
                foreach (var file in receipt.Files)
                {
                    var path = PathPolicy.Target(candidate.DeploymentRoot, file.RelativePath);
                    if (!paths.Add(path)) throw new DeploymentException("TargetConflict", path);
                    held.File(path);
                    if (BlobStore.Hash(path) != file.InstalledHash) throw new DeploymentException("RelocationFilesChanged", path);
                    if (file.OriginalHash is { } original) { held.File(store.Blobs.PathFor(original)); store.Blobs.Verify(original); }
                }
            }
            store.CheckAvailable();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DeploymentException or ArgumentException)
        { issues.Add(new(ex is DeploymentException de ? de.Code : "InspectionFailed", ex.Message)); }
        return new(state.Id, state.Revision, old, candidate, identity, receipts.Length, receipts.Sum(r => r.Files.Length), issues.ToArray());
    }

    private static void AssertOldLocationMissing(string path)
    {
        // An offline drive or denied path does not establish that the old game was moved.
        PathPolicy.Full(path);
        using var volume = DirectoryLease.Acquire(Path.GetPathRoot(path)!);
        try { File.GetAttributes(path); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        throw new DeploymentException("RelocationOldLocationExists", path);
    }

    private sealed class ReadLocks : IDisposable
    {
        private readonly List<IDisposable> locks = [];
        public DirectoryIdentity Directory(string path)
        { var lease = DirectoryLease.Acquire(path); locks.Add(lease); return lease.Identity; }
        public void File(string path)
        {
            PathPolicy.CheckNoLinks(path);
            Directory(Path.GetDirectoryName(path)!);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            locks.Add(stream);
            FileIdentity.AssertSingleLink(stream.SafeFileHandle, path);
        }
        public void Dispose() { for (var i = locks.Count - 1; i >= 0; i--) locks[i].Dispose(); }
    }
}
