using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Storage;

public sealed class WorkspaceStore : IDisposable
{
    private readonly FileStream lease;
    private readonly string statePath;
    private readonly DirectoryLease rootLease;
    private readonly string physicalRoot;
    public string Root { get; }
    public WorkspaceState State { get; private set; }
    public BlobStore Blobs { get; }

    private WorkspaceStore(string root, FileStream lease, WorkspaceState state, DirectoryLease rootLease)
    {
        Root = root; this.lease = lease; State = state;
        this.rootLease = rootLease;
        physicalRoot = rootLease.Identity.Path;
        statePath = Path.Combine(physicalRoot, "workspace.json");
        Blobs = new(physicalRoot);
    }

    public static WorkspaceStore Open(string root, bool create = false, Guid? expectedId = null)
    {
        root = PathPolicy.Full(root);
        var path = Path.Combine(root, "workspace.json");
        if (create)
        {
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new DeploymentException("EmptyDataFolderRequired", root);
            Directory.CreateDirectory(root);
        }
        else if (!File.Exists(path)) throw new DeploymentException("DataUnavailable", root);
        PathPolicy.CheckNoLinks(Path.Combine(root, "workspace.lock"));
        var rootLease = DirectoryLease.Acquire(root);
        path = Path.Combine(rootLease.Identity.Path, "workspace.json");
        FileStream lease;
        try
        {
            if (create && Directory.EnumerateFileSystemEntries(rootLease.Identity.Path).Any())
                throw new DeploymentException("EmptyDataFolderRequired", root);
            lease = new FileStream(Path.Combine(rootLease.Identity.Path, "workspace.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch { rootLease.Dispose(); throw; }
        try
        {
            var state = create ? WorkspaceState.New() : AtomicJson.Read<WorkspaceState>(path);
            if (expectedId.HasValue && state.Id != expectedId) throw new DeploymentException("WrongDataSet", root);
            foreach (var name in new[] { "blobs", "operations", "protection" })
            {
                var folder = Path.Combine(rootLease.Identity.Path, name);
                PathPolicy.CheckNoLinks(folder);
                if (create) Directory.CreateDirectory(folder);
                else if (!Directory.Exists(folder)) throw new DeploymentException("DataUnavailable", folder);
            }
            if (create) AtomicJson.Write(path, state);
            var store = new WorkspaceStore(root, lease, state, rootLease);
            store.CheckAvailable();
            foreach (var receipt in state.Receipts)
                foreach (var file in receipt.Files.Where(f => f.OriginalHash is not null)) store.Blobs.Verify(file.OriginalHash!);
            foreach (var journal in store.Pending().Where(j => j.IntentIndex >= 0))
                foreach (var file in journal.Files.Where(f => f.BeforeHash is not null)) store.Blobs.Verify(file.BeforeHash!);
            return store;
        }
        catch { lease.Dispose(); rootLease.Dispose(); throw; }
    }

    public void CheckAvailable()
    {
        if (DirectoryLease.Inspect(Root) != rootLease.Identity)
            throw new DeploymentException("DataChanged", Root);
        PathPolicy.CheckNoLinks(statePath);
        if (!File.Exists(statePath)) throw new DeploymentException("DataUnavailable", Root);
        var disk = AtomicJson.Read<WorkspaceState>(statePath);
        if (AtomicJson.Fingerprint(disk) != AtomicJson.Fingerprint(State))
            throw new DeploymentException("DataChanged", Root);
    }

    public void Save(WorkspaceState next)
    {
        CheckAvailable();
        if (next.Id != State.Id) throw new DeploymentException("WrongDataSet");
        foreach (var pending in Pending())
        {
            var gameChanged = AtomicJson.Fingerprint(State.Games.SingleOrDefault(g => g.Id == pending.GameId)) !=
                AtomicJson.Fingerprint(next.Games.SingleOrDefault(g => g.Id == pending.GameId));
            var libraryChanged = AtomicJson.Fingerprint(State.Libraries.SingleOrDefault(l => l.Id == pending.LibraryId)) !=
                AtomicJson.Fingerprint(next.Libraries.SingleOrDefault(l => l.Id == pending.LibraryId));
            if (gameChanged || libraryChanged) throw new DeploymentException("PendingConfigurationLocked");
        }
        foreach (var receipt in next.Receipts)
            if (!next.Games.Any(g => g.Id == receipt.GameId) ||
                !next.Libraries.Any(l => l.Id == receipt.LibraryId && l.Versions.Any(v => v.Id == receipt.VersionId)))
                throw new DeploymentException("ConfigurationReferenced");
        next = next with { Revision = State.Revision + 1 };
        AtomicJson.Write(statePath, next);
        State = next;
    }

    public void WriteJournal(OperationJournal journal)
    {
        CheckAvailable();
        var path = Path.Combine(physicalRoot, "operations", journal.Id.ToString("N") + ".json");
        PathPolicy.CheckNoLinks(path);
        AtomicJson.Write(path, journal);
    }

    public OperationJournal[] Pending() => Directory.EnumerateFiles(Path.Combine(physicalRoot, "operations"), "*.json")
        .Select(AtomicJson.Read<OperationJournal>)
        .Where(j => j.Status is OperationStatus.Preparing or OperationStatus.Executing or OperationStatus.NeedsRepair)
        .ToArray();

    public void Protect(ProtectionEntry entry, string? description = null)
    {
        CheckAvailable();
        entry = entry with { ProtectionDirectory = ProtectionLayout.CreateDirectory(entry) };
        var copies = ProtectionLayout.FilesDirectory(Path.Combine(physicalRoot, "protection"), entry);
        PathPolicy.CheckNoLinks(copies);
        Directory.CreateDirectory(copies);
        foreach (var file in entry.Files)
        {
            if (file.BeforeHash is not { } hash) continue;
            Blobs.Verify(hash);
            var copy = PathPolicy.Target(copies, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            using (var input = new FileStream(Blobs.PathFor(hash), FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.WriteThrough))
            { input.CopyTo(output); output.Flush(true); }
            if (BlobStore.Hash(copy) != hash) throw new DeploymentException("VerificationFailed", copy);
        }
        if (entry.FullVersion)
        {
            var info = Path.Combine(Path.GetDirectoryName(copies)!, "备份说明.txt");
            PathPolicy.CheckNoLinks(info);
            using var output = new FileStream(info, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.WriteThrough);
            using var writer = new StreamWriter(output, new System.Text.UTF8Encoding(true), leaveOpen: true);
            writer.Write(description ?? System.Text.Json.JsonSerializer.Serialize(entry, AtomicJson.Options));
            writer.Flush(); output.Flush(true);
        }
        var path = Path.Combine(physicalRoot, "protection", entry.OperationId.ToString("N") + ".json");
        PathPolicy.CheckNoLinks(path);
        AtomicJson.Write(path, entry);
    }

    public void Dispose() { lease.Dispose(); rootLease.Dispose(); }
}
