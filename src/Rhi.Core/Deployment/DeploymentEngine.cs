using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Deployment;

public sealed partial class DeploymentEngine(WorkspaceStore store, Action<string, int>? fault = null, Action<string>? gameSafetyCheck = null)
{
    private readonly object gate = new();

    public IReadOnlyList<OperationResult> Execute(BatchPlan plan, OperationConsent consent,
        IProgress<TaskProgress>? progress = null, CancellationToken cancellation = default)
    {
        lock (gate)
        {
            if (!plan.CanExecute) throw new DeploymentException("PreflightBlocked");
            if (plan.Libraries.Any(p => p.RequiresOverwrite) && !consent.BackupOriginals ||
                plan.Libraries.Any(p => p.RequiresProtection) && !consent.ProtectChanges)
                throw new DeploymentException("ConsentRequired");
            var fresh = new DeploymentPlanner(store).Build(store.State, plan.GameId,
                plan.Libraries.Select(p => p.Request).ToArray());
            if (AtomicJson.Fingerprint(plan) != AtomicJson.Fingerprint(fresh))
                throw new DeploymentException("ChangedSincePreflight");
            var results = new List<OperationResult>();
            var stopped = false;
            foreach (var library in plan.Libraries)
            {
                if (stopped)
                {
                    results.Add(new(library.Request.LibraryId, OperationStatus.NotExecuted, "NotExecuted"));
                    continue;
                }
                BatchPlan current;
                try { current = new DeploymentPlanner(store).Build(store.State, plan.GameId, [library.Request]); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DeploymentException or InvalidOperationException)
                {
                    results.Add(new(library.Request.LibraryId, OperationStatus.NotExecuted,
                        ex is DeploymentException de ? de.Code : "InspectionFailed"));
                    stopped = true;
                    continue;
                }
                if (!current.CanExecute || current.ExeHash != plan.ExeHash ||
                    current.TargetIdentity != plan.TargetIdentity ||
                    !SameLibraryPlan(library, current.Libraries.Single()))
                {
                    results.Add(new(library.Request.LibraryId, OperationStatus.NotExecuted, "ChangedSincePreflight"));
                    stopped = true;
                    continue;
                }
                var result = ExecuteLibrary(plan, library, progress, cancellation);
                results.Add(result);
                stopped = result.Status != OperationStatus.Succeeded;
                fault?.Invoke("LibraryCompleted", results.Count - 1);
            }
            return results;
        }
    }

    private OperationResult ExecuteLibrary(BatchPlan batch, LibraryPlan library,
        IProgress<TaskProgress>? progress, CancellationToken cancellation)
    {
        var id = Guid.NewGuid();
        var journal = new OperationJournal(id, store.State.Id, batch.GameId, library.Request.LibraryId,
            library.Request.Kind, batch.TargetRoot, batch.ExePath, batch.ExeHash, OperationStatus.Preparing,
            library.Changes.Select(c => new JournalFile(c.RelativePath, c.BeforeHash, c.DesiredHash, c.ParentIdentity)).ToArray(),
            [], -1, null, DateTimeOffset.UtcNow, batch.TargetIdentity, FileProtocol: 1,
            PreviouslyCreatedDirectories: library.PreviouslyCreatedDirectories);
        var started = false;
        try
        {
            using var targetLease = DirectoryLease.Acquire(batch.TargetRoot);
            if (batch.TargetIdentity is null || targetLease.Identity != batch.TargetIdentity)
                throw new DeploymentException("GameIdentityChanged", batch.TargetRoot);
            cancellation.ThrowIfCancellationRequested();
            store.WriteJournal(journal);
            started = true;
            progress?.Report(new("Preparing", library.LibraryName, 0, library.Changes.Length));
            EnsureIdentity(journal);
            foreach (var input in library.Inputs ?? [])
                if (BlobStore.Hash(input.Path) != input.Hash)
                    throw new DeploymentException("ChangedSincePreflight", input.Path);
            foreach (var change in library.Changes)
            {
                cancellation.ThrowIfCancellationRequested();
                var target = PathPolicy.Target(batch.TargetRoot, change.RelativePath);
                if (BlobStore.Hash(target) != change.BeforeHash) throw new DeploymentException("ChangedSincePreflight", target);
                if (change.BeforeHash is { } before) store.Blobs.Capture(target, before);
                if (change.GeneratedContent is { } content) store.Blobs.CaptureGenerated(content, change.DesiredHash!);
                else if (change.SourcePath is { } source) store.Blobs.Capture(source, change.DesiredHash!);
                else if (change.DesiredHash is { } desired) store.Blobs.Verify(desired);
            }
            var modified = library.Changes.Where(c => c.ExternalChange && c.BeforeHash is not null)
                .Select(c => new JournalFile(c.RelativePath, c.BeforeHash, c.DesiredHash)).ToArray();
            if (modified.Length > 0) store.Protect(new(id, batch.GameId, library.Request.LibraryId,
                batch.TargetRoot, modified, DateTimeOffset.UtcNow));
            journal = journal with { Status = OperationStatus.Executing };
            store.WriteJournal(journal);
            foreach (var relative in FileWriter.MissingDirectories(targetLease.Identity.Path, library.Changes))
            {
                cancellation.ThrowIfCancellationRequested();
                var directory = PathPolicy.Target(targetLease.Identity.Path, relative);
                if (Directory.Exists(directory)) continue;
                Directory.CreateDirectory(directory);
                journal = journal with { CreatedDirectories = [.. journal.CreatedDirectories, relative] };
                store.WriteJournal(journal);
            }
            for (var i = 0; i < library.Changes.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                EnsureIdentity(journal);
                var change = library.Changes[i];
                journal = journal with { IntentIndex = i };
                store.WriteJournal(journal);
                fault?.Invoke("BeforeWrite", i);
                FileReplacement.Apply(store.Blobs, journal, i, fault);
                fault?.Invoke("AfterWrite", i);
                progress?.Report(new("Executing", library.LibraryName, i + 1, library.Changes.Length));
            }
            cancellation.ThrowIfCancellationRequested();
            foreach (var change in library.Changes)
                if (BlobStore.Hash(PathPolicy.Target(batch.TargetRoot, change.RelativePath)) != change.DesiredHash)
                    throw new DeploymentException("VerificationFailed", change.RelativePath);
            EnsureIdentity(journal);
            Commit(batch, library, journal);
            for (var i = 0; i <= journal.IntentIndex; i++) FileReplacement.Cleanup(journal, i);
            FileWriter.CleanEmptyDirectories(targetLease.Identity.Path,
                library.PreviouslyCreatedDirectories.Union(journal.CreatedDirectories));
            journal = journal with { Status = OperationStatus.Succeeded };
            store.WriteJournal(journal);
            return new(library.Request.LibraryId, OperationStatus.Succeeded, "Succeeded", id);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (store.State.CommittedOperations.Contains(id))
                return new(library.Request.LibraryId, OperationStatus.Succeeded, "CommittedJournalPending", id);
            if (!started) return new(library.Request.LibraryId,
                ex is OperationCanceledException ? OperationStatus.Cancelled : OperationStatus.Restored, ex.Message, id);
            progress?.Report(new("Recovering", library.LibraryName, 0, journal.Files.Length));
            return Rollback(journal, ex is OperationCanceledException, ex.Message);
        }
    }

    private void Commit(BatchPlan batch, LibraryPlan library, OperationJournal journal)
    {
        var receipts = store.State.Receipts.Where(r => !(r.GameId == batch.GameId && r.LibraryId == library.Request.LibraryId)).ToList();
        var old = store.State.Receipts.SingleOrDefault(r => r.GameId == batch.GameId && r.LibraryId == library.Request.LibraryId);
        if (library.Request.Kind != OperationKind.Clear)
        {
            var files = library.Changes.Where(c => c.IsManagedAfter).Select(c => new ManagedFile(c.RelativePath,
                c.DesiredHash!, old?.Files.Any(f => PathPolicy.Comparer.Equals(f.RelativePath, c.RelativePath)) == true
                    ? c.OriginalHash : c.BeforeHash)).ToArray();
            receipts.Add(new(batch.GameId, library.Request.LibraryId, library.Request.VersionId!.Value,
                library.VersionLabel, batch.TargetRoot, batch.ExeHash, files,
                library.PreviouslyCreatedDirectories.Union(journal.CreatedDirectories, PathPolicy.Comparer).ToArray(), batch.TargetIdentity));
        }
        fault?.Invoke("BeforeCommit", 0);
        store.Save(store.State with { Receipts = receipts.ToArray(),
            CommittedOperations = [.. store.State.CommittedOperations, journal.Id] });
    }

    private void EnsureIdentity(OperationJournal journal)
    {
        gameSafetyCheck?.Invoke(journal.ExePath);
        if (!Directory.Exists(journal.TargetRoot) || journal.TargetIdentity is null ||
            DirectoryLease.Inspect(journal.TargetRoot) != journal.TargetIdentity ||
            BlobStore.Hash(journal.ExePath) != journal.ExeHash)
            throw new DeploymentException("GameIdentityChanged", journal.TargetRoot);
        PathPolicy.Disjoint(journal.TargetRoot, store.Root);
        foreach (var source in store.State.Libraries.SelectMany(l => l.Versions).Select(v => v.SourcePath))
            PathPolicy.DisjointSource(source, journal.TargetRoot);
    }

    private static bool SameLibraryPlan(LibraryPlan expected, LibraryPlan actual)
    {
        if (expected.Changes.Length != actual.Changes.Length) return false;
        // An earlier successful library may have created a shared parent directory.
        // Keep the originally inspected ancestor, whose identity is rechecked before mutation.
        var changes = actual.Changes.Select((c, i) => c with { ParentIdentity = expected.Changes[i].ParentIdentity }).ToArray();
        return AtomicJson.Fingerprint(expected) == AtomicJson.Fingerprint(actual with { Changes = changes });
    }
}
