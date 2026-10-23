using Rhi.Core.Models;
using Rhi.Core.Deployment.Resources;
using System.Security.Cryptography;
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
            if (!File.Exists(game.ExePath)) throw new DeploymentException("GameUnavailable", game.ExePath);
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
                    receipt.TargetIdentity != targetIdentity))
                    throw new DeploymentException("GameIdentityChanged", game.Name);

                var version = request.Kind == OperationKind.Clear ? null : library.Versions.Single(v => v.Id == request.VersionId);
                var mapping = version is null ? new ResourceMap(new(PathPolicy.Comparer), [])
                    : ResourceMapping.Map(library, version, game);
                var desired = mapping.Files;
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
                    desired.TryGetValue(relative, out var resource);
                    var source = resource?.SourcePath;
                    if (managed?.OriginalHash is { } original) store.Blobs.Verify(original);
                    var wanted = resource?.Content is { } content ? Convert.ToHexString(SHA256.HashData(content))
                        : source is null ? managed?.OriginalHash : BlobStore.Hash(source)
                            ?? throw new DeploymentException("SourceUnavailable", source);
                    changes.Add(new(relative, source, wanted, before, managed?.OriginalHash,
                        resource is not null, managed is not null && before != managed.InstalledHash,
                        DirectoryLease.ExistingParent(target), resource?.Content, resource?.Edits,
                        source is null ? null : Stamp(source), Stamp(target)));
                }
                foreach (var input in mapping.Inputs)
                    if (BlobStore.Hash(input.Path) != input.Hash)
                        throw new DeploymentException("ChangedSincePreflight", input.Path);
                plans.Add(new(request, library.Name, version?.Label ?? receipt!.VersionLabel, changes.ToArray(),
                    receipt?.CreatedDirectories ?? [],
                    changes.Any(c => c.BeforeHash is not null && !old.ContainsKey(c.RelativePath)),
                    changes.Any(c => c.ExternalChange), mapping.Inputs));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DeploymentException or InvalidOperationException)
            { AddIssue(issues, ex); }
        }
        // Parent-child collisions must also be detected across libraries before any write.
        var allTargets = targets.Keys.Order(PathPolicy.Comparer).ToArray();
        var targetSet = new HashSet<string>(allTargets, PathPolicy.Comparer);
        var parentConflicts = new HashSet<string>(PathPolicy.Comparer);
        foreach (var target in allTargets)
            for (var parent = Path.GetDirectoryName(target); parent is not null; parent = Path.GetDirectoryName(parent))
                if (targetSet.Contains(parent)) parentConflicts.Add(parent);
        issues.AddRange(parentConflicts.Order(PathPolicy.Comparer).Select(path => new PlanIssue("FileDirectoryConflict", path)));
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
            long targetBytes = 0, backupBytes = 0;
            foreach (var plan in plans)
            foreach (var change in plan.Changes)
            {
                if (change.SourcePath is not null) targetBytes = checked(targetBytes + new FileInfo(change.SourcePath).Length);
                if (change.GeneratedContent is not null) targetBytes = checked(targetBytes + change.GeneratedContent.LongLength);
                if (change.SourcePath is null && change.GeneratedContent is null && change.DesiredHash is { } hash)
                    targetBytes = checked(targetBytes + new FileInfo(store.Blobs.PathFor(hash)).Length);
                var target = PathPolicy.Target(targetRoot, change.RelativePath);
                if ((plan.RequiresOverwrite || plan.RequiresProtection) && File.Exists(target))
                    backupBytes = checked(backupBytes + new FileInfo(target).Length * (plan.RequiresProtection ? 2 : 1));
            }
            var volumes = new Dictionary<string, long>(PathPolicy.Comparer);
            volumes[Path.GetPathRoot(targetRoot)!] = targetBytes;
            var dataVolume = Path.GetPathRoot(store.Root)!;
            volumes[dataVolume] = checked(volumes.GetValueOrDefault(dataVolume) + backupBytes + 1024 * 1024);
            foreach (var (volume, required) in volumes)
                if (new DriveInfo(volume).AvailableFreeSpace < required) issues.Add(new("InsufficientSpace", volume));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        { issues.Add(new("SpaceCheckFailed", ex.Message)); }
    }

    private static void AddIssue(List<PlanIssue> issues, Exception ex) =>
        issues.Add(new(ex is DeploymentException de ? de.Code : "InspectionFailed", ex.Message));

    internal static FileStamp? Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new(info.Length, info.LastWriteTimeUtc.Ticks) : null;
    }
}
