using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Deployment;

public sealed partial class DeploymentEngine(WorkspaceStore store, Action<string, int>? fault = null,
    Action<string>? gameSafetyCheck = null, Func<ProtectionEntry, string>? protectionDescription = null)
{
    private readonly object gate = new();

    public IReadOnlyList<OperationResult> Execute(BatchPlan plan, OperationConsent consent,
        IProgress<TaskProgress>? progress = null, CancellationToken cancellation = default)
    {
        lock (gate)
        {
            if (!plan.CanExecute) throw new DeploymentException("PreflightBlocked");
            if (consent.DiscardChangesOnClear && (consent.ProtectChanges ||
                plan.Libraries.Any(p => p.Request.Kind != OperationKind.Clear)))
                throw new DeploymentException("ConsentRequired");
            if (plan.Libraries.Any(p => p.RequiresOverwrite) && !consent.BackupOriginals ||
                plan.Libraries.Any(p => p.RequiresProtection) && !consent.ProtectChanges && !consent.DiscardChangesOnClear)
                throw new DeploymentException("ConsentRequired");
            ValidateApprovedPlan(plan);
            gameSafetyCheck?.Invoke(plan.ExePath);
            var results = new List<OperationResult>();
            var stopped = false;
            foreach (var library in plan.Libraries)
            {
                if (stopped)
                {
                    results.Add(new(library.Request.LibraryId, OperationStatus.NotExecuted, "NotExecuted"));
                    continue;
                }
                try { store.CheckAvailable(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DeploymentException or InvalidOperationException)
                {
                    results.Add(new(library.Request.LibraryId, OperationStatus.NotExecuted,
                        ex is DeploymentException de ? de.Code : "InspectionFailed"));
                    stopped = true;
                    continue;
                }
                var result = ExecuteLibrary(plan, library, consent, progress, cancellation);
                results.Add(result);
                stopped = result.Status != OperationStatus.Succeeded;
                fault?.Invoke("LibraryCompleted", results.Count - 1);
            }
            return results;
        }
    }

    private OperationResult ExecuteLibrary(BatchPlan batch, LibraryPlan library, OperationConsent consent,
        IProgress<TaskProgress>? progress, CancellationToken cancellation)
    {
        var id = Guid.NewGuid();
        var journal = new OperationJournal(id, store.State.Id, batch.GameId, library.Request.LibraryId,
            library.Request.Kind, batch.TargetRoot, batch.ExePath, batch.ExeHash, OperationStatus.Preparing,
            library.Changes.Select(c => new JournalFile(c.RelativePath, c.BeforeHash, c.DesiredHash, c.ParentIdentity)).ToArray(),
            [], -1, null, DateTimeOffset.UtcNow, batch.TargetIdentity, FileProtocol: 2,
            PreviouslyCreatedDirectories: library.PreviouslyCreatedDirectories);
        var started = false;
        var parentLeases = new List<DirectoryLease>();
        var parked = new List<FileStream>();
        var publications = new PublicationBatch(fault);
        try
        {
            using var targetLease = DirectoryLease.Acquire(batch.TargetRoot);
            if (batch.TargetIdentity is null || targetLease.Identity != batch.TargetIdentity)
                throw new DeploymentException("GameIdentityChanged", batch.TargetRoot);
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(new("Preparing", library.LibraryName, 0, library.Changes.Length));
            var oldReceipt = store.State.Receipts.SingleOrDefault(r => r.GameId == batch.GameId && r.LibraryId == library.Request.LibraryId);
            var managed = new HashSet<string>(oldReceipt?.Files.Select(f => f.RelativePath) ?? [], PathPolicy.Comparer);
            foreach (var change in library.Changes)
            {
                cancellation.ThrowIfCancellationRequested();
                // Persist originals and explicitly requested protection only. The source
                // goes straight to the game volume; existing managed files are parked
                // there by FileReplacement until this library commits.
                var original = change.IsManagedAfter && !managed.Contains(change.RelativePath);
                var protection = library.RequiresProtection && consent.ProtectChanges && managed.Contains(change.RelativePath);
                if (change.BeforeHash is { } before && (original || protection))
                    store.Blobs.Capture(PathPolicy.Target(batch.TargetRoot, change.RelativePath), before);
            }
            if (library.RequiresProtection && consent.ProtectChanges)
            {
                // The old receipt owns the snapshot scope and identity, including on version changes.
                var receipt = store.State.Receipts.Single(r => r.GameId == batch.GameId && r.LibraryId == library.Request.LibraryId);
                var changes = library.Changes.ToDictionary(c => c.RelativePath, StringComparer.OrdinalIgnoreCase);
                var files = receipt.Files.Select(f => changes[f.RelativePath])
                    .Select(c => new JournalFile(c.RelativePath, c.BeforeHash, c.DesiredHash)).ToArray();
                var entry = new ProtectionEntry(id, batch.GameId, library.Request.LibraryId, batch.TargetRoot, files,
                    DateTimeOffset.UtcNow, store.State.Games.Single(g => g.Id == batch.GameId).Name,
                    library.LibraryName, VersionId: receipt.VersionId, VersionLabel: receipt.VersionLabel,
                    Kind: library.Request.Kind, ModifiedFiles: files.Where(f => changes[f.RelativePath].ExternalChange)
                        .Select(f => f.RelativePath).ToArray(), FullVersion: true);
                store.Protect(entry, protectionDescription?.Invoke(entry));
            }
            foreach (var relative in FileWriter.MissingDirectories(targetLease.Identity.Path, library.Changes))
            {
                cancellation.ThrowIfCancellationRequested();
                var directory = PathPolicy.Target(targetLease.Identity.Path, relative);
                if (Directory.Exists(directory)) continue;
                Directory.CreateDirectory(directory);
                journal = journal with { CreatedDirectories = [.. journal.CreatedDirectories, relative] };
            }
            // Hold each distinct parent once for the batch, rather than reopening its
            // full ancestor chain for every copy and every cleanup.
            var heldParents = new Dictionary<string, DirectoryIdentity>(PathPolicy.Comparer)
                { [targetLease.Identity.Path] = targetLease.Identity };
            foreach (var expected in library.Changes.Select(c => c.ParentIdentity).Distinct())
            {
                if (expected is null) throw new DeploymentException("GameIdentityChanged");
                if (!heldParents.TryGetValue(expected.Path, out var actual))
                {
                    var lease = DirectoryLease.Acquire(expected.Path);
                    parentLeases.Add(lease);
                    actual = lease.Identity;
                    heldParents.Add(expected.Path, actual);
                }
                if (actual != expected) throw new DeploymentException("GameIdentityChanged", expected.Path);
            }
            foreach (var parent in library.Changes.Select(c => Path.GetDirectoryName(
                Path.Combine(targetLease.Identity.Path, PathPolicy.Relative(c.RelativePath)))!).Distinct(PathPolicy.Comparer))
            {
                if (heldParents.ContainsKey(parent)) continue;
                var lease = DirectoryLease.Acquire(parent);
                parentLeases.Add(lease);
                heldParents.Add(parent, lease.Identity);
            }
            // One durable plan before copying; all planned paths remain recoverable on
            // restart. Progress below is memory-only, never a full JSON rewrite per file.
            journal = journal with { Status = OperationStatus.Executing, IntentIndex = journal.Files.Length - 1 };
            store.WriteJournal(journal);
            started = true;
            for (var i = 0; i < library.Changes.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var change = library.Changes[i];
                journal = journal with { IntentIndex = i };
                fault?.Invoke("BeforeWrite", i);
                var published = FileReplacement.Apply(store.Blobs, journal, i, fault, parked, change, parentHeld: true);
                var hasPublication = published is not null;
                try
                {
                    publications.Add(published, i);
                    published = null; // Add succeeded: only the batch now owns it.
                }
                finally { published?.Dispose(); }
                if (hasPublication) fault?.Invoke("PublishPendingFlush", i);
                if (publications.IsFull) publications.Drain(Completed);
            }
            publications.Drain(Completed);
            // Cancellation cannot abandon active barriers. Drain joins and closes
            // everything first; cancellation still prevents the library commit.
            cancellation.ThrowIfCancellationRequested();
            Commit(batch, library, journal);
            // Normal execution owns only these parked originals; staging has already
            // been renamed. Recovery alone inspects names after ownership was lost.
            foreach (var original in parked) NativeFile.Delete(original);
            foreach (var original in parked) original.Dispose();
            parked.Clear();
            foreach (var lease in parentLeases) lease.Dispose();
            parentLeases.Clear();
            FileWriter.CleanEmptyDirectories(targetLease.Identity.Path,
                library.PreviouslyCreatedDirectories.Union(journal.CreatedDirectories));
            journal = journal with { Status = OperationStatus.Succeeded };
            store.WriteJournal(journal);
            return new(library.Request.LibraryId, OperationStatus.Succeeded, "Succeeded", id);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Drain never returns with running workers. Earlier copy/cancellation
            // errors can leave an unflushed batch, which must close before rollback.
            Exception failure = ex;
            try { publications.Dispose(); }
            catch (Exception closeError) when (closeError is not OutOfMemoryException and not StackOverflowException)
            { failure = new AggregateException(ex, closeError); }
            foreach (var original in parked) original.Dispose();
            parked.Clear();
            foreach (var lease in parentLeases) lease.Dispose();
            parentLeases.Clear();
            if (store.State.CommittedOperations.Contains(id))
                return new(library.Request.LibraryId, OperationStatus.Succeeded, "CommittedJournalPending", id);
            if (!started)
            {
                FileWriter.CleanEmptyDirectories(batch.TargetRoot, journal.CreatedDirectories);
                return new(library.Request.LibraryId,
                    ex is OperationCanceledException ? OperationStatus.Cancelled : OperationStatus.Restored, failure.Message, id);
            }
            progress?.Report(new("Recovering", library.LibraryName, 0, journal.Files.Length));
            return Rollback(journal, ex is OperationCanceledException, failure.Message);
        }
        finally
        {
            try { publications.Dispose(); }
            finally
            {
                foreach (var original in parked) original.Dispose();
                foreach (var lease in parentLeases) lease.Dispose();
            }
        }

        void Completed(int index)
        {
            // Completion callbacks can fail after later files in this batch were
            // applied. Keep journal.IntentIndex at the last actual Apply, not index.
            fault?.Invoke("AfterWrite", index);
            progress?.Report(new("Executing", library.LibraryName, index + 1, library.Changes.Length));
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
            (journal.FileProtocol < 2 && !string.IsNullOrEmpty(journal.ExeHash) && BlobStore.Hash(journal.ExePath) != journal.ExeHash))
            throw new DeploymentException("GameIdentityChanged", journal.TargetRoot);
        PathPolicy.Disjoint(journal.TargetRoot, store.Root);
        foreach (var source in store.State.Libraries.SelectMany(l => l.Versions).Select(v => v.SourcePath))
            PathPolicy.DisjointSource(source, journal.TargetRoot);
    }

    private void ValidateApprovedPlan(BatchPlan plan)
    {
        store.CheckAvailable();
        if (plan.WorkspaceId != store.State.Id || plan.Revision != store.State.Revision ||
            DirectoryLease.Inspect(plan.TargetRoot) != plan.TargetIdentity)
            throw new DeploymentException("ChangedSincePreflight");
        foreach (var library in plan.Libraries)
        {
            foreach (var input in library.Inputs ?? [])
                if (BlobStore.Hash(input.Path) != input.Hash) throw new DeploymentException("ChangedSincePreflight", input.Path);
            foreach (var change in library.Changes)
            {
                var target = PathPolicy.Target(plan.TargetRoot, change.RelativePath);
                if (DeploymentPlanner.Stamp(target) != change.TargetStamp ||
                    change.SourcePath is { } source && DeploymentPlanner.Stamp(source) != change.SourceStamp)
                    throw new DeploymentException("ChangedSincePreflight", target);
                if (change.GeneratedContent is { } content &&
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)) != change.DesiredHash)
                    throw new DeploymentException("ChangedSincePreflight", target);
            }
        }
    }
}
