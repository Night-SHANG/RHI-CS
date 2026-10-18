using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Deployment;

public sealed class DeploymentPlanner(WorkspaceStore store)
{
    public BatchPlan Build(WorkspaceState state, Guid gameId, DeploymentRequest[] requests)
    {
        store.CheckAvailable();
        var game = state.Games.Single(g => g.Id == gameId);
        var issues = new List<PlanIssue>();
        var plans = new List<LibraryPlan>();
        var targets = new Dictionary<string, Guid>(PathPolicy.Comparer);
        var exeHash = "";
        DirectoryIdentity? targetIdentity = null;
        try
        {
            ValidateRoots(state, game);
            targetIdentity = DirectoryLease.Inspect(game.DeploymentRoot);
            exeHash = BlobStore.Hash(game.ExePath) ?? throw new DeploymentException("GameUnavailable", game.ExePath);
            if (store.Pending().Any(j => j.GameId == game.Id && !state.CommittedOperations.Contains(j.Id)))
                throw new DeploymentException("NeedsRepair", game.Name);
            foreach (var receipt in state.Receipts)
                foreach (var file in receipt.Files)
                {
                    var target = PathPolicy.Target(receipt.TargetRoot, file.RelativePath);
                    if (!targets.TryAdd(target, receipt.LibraryId)) throw new DeploymentException("TargetConflict", target);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DeploymentException)
        { AddIssue(issues, ex); }

        var seen = new HashSet<Guid>();
        foreach (var request in requests)
        {
            try
            {
                if (!seen.Add(request.LibraryId)) throw new DeploymentException("DuplicateLibrary");
                var library = state.Libraries.Single(l => l.Id == request.LibraryId);
                var receipt = state.Receipts.SingleOrDefault(r => r.GameId == gameId && r.LibraryId == library.Id);
                if (request.Kind == OperationKind.Deploy && receipt is not null ||
                    request.Kind != OperationKind.Deploy && receipt is null)
                    throw new DeploymentException("StateChanged", library.Name);
                if (receipt is not null && (!PathPolicy.Comparer.Equals(receipt.TargetRoot, game.DeploymentRoot) ||
                    receipt.ExeHash != exeHash || receipt.TargetIdentity != targetIdentity))
                    throw new DeploymentException("GameIdentityChanged", game.Name);

                var version = request.Kind == OperationKind.Clear ? null : library.Versions.Single(v => v.Id == request.VersionId);
                var desired = version is null ? new Dictionary<string, string>(PathPolicy.Comparer) : Map(library, version);
                var old = receipt?.Files.ToDictionary(f => f.RelativePath, PathPolicy.Comparer)
                    ?? new Dictionary<string, ManagedFile>(PathPolicy.Comparer);
                var paths = old.Keys.Union(desired.Keys, PathPolicy.Comparer).Order(PathPolicy.Comparer);
                var changes = new List<FileChange>();
                foreach (var relative in paths)
                {
                    var target = PathPolicy.Target(game.DeploymentRoot, relative);
                    if (PathPolicy.Comparer.Equals(DirectoryLease.PhysicalPath(target), DirectoryLease.PhysicalPath(game.ExePath)))
                        throw new DeploymentException("GameExecutableTarget", target);
                    if (targets.TryGetValue(target, out var owner) && owner != library.Id)
                        throw new DeploymentException("TargetConflict", target);
                    targets[target] = library.Id;
                    CheckParents(target, game.DeploymentRoot);
                    FileIdentity.AssertSingleLink(target);
                    var before = BlobStore.Hash(target);
                    old.TryGetValue(relative, out var managed);
                    desired.TryGetValue(relative, out var source);
                    if (managed?.OriginalHash is { } original) store.Blobs.Verify(original);
                    var wanted = source is null ? managed?.OriginalHash : BlobStore.Hash(source)
                        ?? throw new DeploymentException("SourceUnavailable", source);
                    changes.Add(new(relative, source, wanted, before, managed?.OriginalHash,
                        source is not null, managed is not null && before != managed.InstalledHash,
                        DirectoryLease.ExistingParent(target)));
                }
                plans.Add(new(request, library.Name, version?.Label ?? receipt!.VersionLabel, changes.ToArray(),
                    receipt?.CreatedDirectories ?? [],
                    changes.Any(c => c.BeforeHash is not null && !old.ContainsKey(c.RelativePath)),
                    changes.Any(c => c.ExternalChange && c.BeforeHash is not null)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DeploymentException or InvalidOperationException)
            { AddIssue(issues, ex); }
        }
        // Parent-child collisions must also be detected across libraries before any write.
        var allTargets = targets.Keys.Order(PathPolicy.Comparer).ToArray();
        for (var i = 0; i < allTargets.Length; i++)
            for (var j = i + 1; j < allTargets.Length; j++)
                if (PathPolicy.Within(allTargets[j], allTargets[i]) || PathPolicy.Within(allTargets[i], allTargets[j]))
                    issues.Add(new("FileDirectoryConflict", allTargets[i]));
        CheckSpace(plans, game.DeploymentRoot, issues);
        return new(state.Id, state.Revision, gameId, game.DeploymentRoot, game.ExePath, exeHash,
            plans.ToArray(), issues.ToArray(), targetIdentity);
    }

    private void ValidateRoots(WorkspaceState state, GameEntry game)
    {
        if (!Directory.Exists(game.DeploymentRoot) || !Directory.Exists(game.InstallRoot))
            throw new DeploymentException("GameUnavailable", game.DeploymentRoot);
        PathPolicy.Disjoint(store.Root, game.InstallRoot);
        PathPolicy.Disjoint(store.Root, game.DeploymentRoot);
        foreach (var other in state.Games.Where(g => g.Id != game.Id))
            PathPolicy.Disjoint(other.DeploymentRoot, game.DeploymentRoot);
        foreach (var source in state.Libraries.SelectMany(l => l.Versions).Select(v => v.SourcePath))
        {
            PathPolicy.DisjointSource(source, store.Root);
            PathPolicy.DisjointSource(source, game.InstallRoot);
            PathPolicy.DisjointSource(source, game.DeploymentRoot);
        }
    }

    private static Dictionary<string, string> Map(ResourceLibrary library, ResourceVersion version)
    {
        if (version.Kind != ResourceKind.Files) throw new DeploymentException("AdapterNotAvailable", version.Kind.ToString());
        var source = PathPolicy.Full(version.SourcePath);
        var prefix = PathPolicy.Relative(version.TargetOverride ?? library.TargetRelative);
        var map = new Dictionary<string, string>(PathPolicy.Comparer);
        if (File.Exists(source)) map.Add(Path.Combine(prefix, Path.GetFileName(source)), source);
        else if (Directory.Exists(source))
            foreach (var file in PathPolicy.Files(source))
            {
                var relative = PathPolicy.Relative(Path.Combine(prefix, Path.GetRelativePath(source, file)));
                if (!map.TryAdd(relative, file)) throw new DeploymentException("TargetConflict", relative);
            }
        else throw new DeploymentException("SourceUnavailable", source);
        if (map.Count == 0) throw new DeploymentException("EmptySource", source);
        return map;
    }

    private static void CheckParents(string target, string root)
    {
        var parent = Path.GetDirectoryName(target);
        while (parent is not null && PathPolicy.Within(parent, root))
        {
            if (File.Exists(parent)) throw new DeploymentException("FileDirectoryConflict", parent);
            parent = Path.GetDirectoryName(parent);
        }
    }

    private void CheckSpace(List<LibraryPlan> plans, string targetRoot, List<PlanIssue> issues)
    {
        try
        {
            long bytes = 0;
            foreach (var change in plans.SelectMany(p => p.Changes))
            {
                if (change.SourcePath is not null) bytes = checked(bytes + new FileInfo(change.SourcePath).Length);
                var target = PathPolicy.Target(targetRoot, change.RelativePath);
                if (File.Exists(target)) bytes = checked(bytes + new FileInfo(target).Length);
            }
            var required = checked(bytes * 3 + 16 * 1024 * 1024);
            foreach (var volume in new[] { Path.GetPathRoot(store.Root)!, Path.GetPathRoot(targetRoot)! }.Distinct(PathPolicy.Comparer))
                if (new DriveInfo(volume).AvailableFreeSpace < required) issues.Add(new("InsufficientSpace", volume));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        { issues.Add(new("SpaceCheckFailed", ex.Message)); }
    }

    private static void AddIssue(List<PlanIssue> issues, Exception ex) =>
        issues.Add(new(ex is DeploymentException de ? de.Code : "InspectionFailed", ex.Message));
}
