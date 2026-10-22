using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.Core.Configuration;

public sealed record LibraryPathMatch(Guid VersionId, string Label, string Before, string After, string Status);
public sealed record LibraryRelocationPlan(Guid LibraryId, string OriginalFingerprint, string Root, LibraryPathMatch[] Matches);

// Only configuration paths change. Receipts, blobs and the user's source files are never moved.
public static class LibraryRelocation
{
    public static LibraryRelocationPlan Preview(WorkspaceState state, string dataRoot, Guid libraryId, string destination)
    {
        var library = state.Libraries.Single(l => l.Id == libraryId);
        if (string.IsNullOrWhiteSpace(library.SourceRoot)) throw new DeploymentException("LibraryRelocationNoRoot");
        var root = ConfigurationRules.LibraryDirectory(state, dataRoot, destination);
        var oldRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(library.SourceRoot));
        var matches = library.Versions.Select(version =>
        {
            var before = Path.GetFullPath(version.SourcePath);
            if (!PathPolicy.Within(before, oldRoot))
                return new LibraryPathMatch(version.Id, version.Label, before, before, "RelocationOutsideRoot");
            var relative = Path.GetRelativePath(oldRoot, before);
            var after = relative == "." ? root : PathPolicy.Target(root, relative);
            try
            {
                ConfigurationRules.Version(state, dataRoot, version with { SourcePath = after });
                return new LibraryPathMatch(version.Id, version.Label, before, after, "RelocationMatched");
            }
            catch (DeploymentException ex) when (ex.Code is "SourcePathMissing" or "ReShadeInvalidBinary")
            { return new LibraryPathMatch(version.Id, version.Label, before, after, "RelocationMissing"); }
        }).ToArray();
        return new(libraryId, AtomicJson.Fingerprint(library), root, matches);
    }

    public static WorkspaceState Apply(WorkspaceState state, string dataRoot, LibraryRelocationPlan plan)
    {
        var fresh = Preview(state, dataRoot, plan.LibraryId, plan.Root);
        if (AtomicJson.Fingerprint(fresh) != AtomicJson.Fingerprint(plan))
            throw new DeploymentException("ChangedSincePreflight");
        var paths = plan.Matches.Where(m => m.Status == "RelocationMatched").ToDictionary(m => m.VersionId);
        return state with { Libraries = state.Libraries.Select(l => l.Id != plan.LibraryId ? l : l with
        {
            SourceRoot = plan.Root,
            Versions = l.Versions.Select(v => paths.TryGetValue(v.Id, out var match) ? v with { SourcePath = match.After } : v).ToArray()
        }).ToArray() };
    }
}
