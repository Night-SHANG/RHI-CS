using System.Text.Json;
using Rhi.App.Services;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.UiSmoke.Seed;

internal sealed record RelocationMetadata(string Data, string OldTarget, string NewTarget, string NewExe,
    string Source, string SourceHash, Guid LibraryId, string TargetHash);

internal static class RelocationFixture
{
    public static void Prepare(string metadataPath)
    {
        var pointer = Bootstrap.Read() ?? throw new InvalidOperationException("Missing fixture.");
        if (!PathPolicy.Within(pointer.Root, Path.Combine(Path.GetTempPath(), "RhiTests"))) throw new InvalidOperationException("Not a CI fixture.");
        using var store = WorkspaceStore.Open(pointer.Root);
        var game = store.State.Games.Single();
        var receipt = store.State.Receipts.Single();
        if (game.Name != "CI Game" || store.Pending().Length != 0) throw new InvalidOperationException("Unexpected fixture.");
        var library = store.State.Libraries.Single(l => l.Id == receipt.LibraryId);
        var source = library.Versions.Single(v => v.Id == receipt.VersionId).SourcePath;
        var moved = Path.Combine(Directory.GetParent(store.Root)!.FullName, "moved-game");
        if (Directory.Exists(moved)) throw new InvalidOperationException("Fixture target already exists.");
        store.Save(store.State with { Preferences = store.State.Preferences with { Theme = "Light", Language = "zh-CN" } });
        Directory.Move(game.DeploymentRoot, moved);
        var data = new RelocationMetadata(store.Root, game.DeploymentRoot, moved, Path.Combine(moved, Path.GetFileName(game.ExePath)),
            source, BlobStore.Hash(source)!, receipt.LibraryId, receipt.Files.Single().InstalledHash);
        using var stream = new FileStream(metadataPath, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, data, AtomicJson.Options);
    }

    public static void Verify(string metadataPath)
    {
        var metadata = AtomicJson.Read<RelocationMetadata>(metadataPath);
        if (!PathPolicy.Within(metadata.Data, Path.Combine(Path.GetTempPath(), "RhiTests"))) throw new InvalidOperationException("Not a CI fixture.");
        using var store = WorkspaceStore.Open(metadata.Data);
        if (store.State.Games.Single().DeploymentRoot != metadata.NewTarget || store.State.Receipts.Length != 0 ||
            Directory.Exists(metadata.OldTarget) || File.Exists(Path.Combine(metadata.NewTarget, "shared.txt")) ||
            BlobStore.Hash(metadata.Source) != metadata.SourceHash ||
            File.ReadAllText(Path.Combine(metadata.NewTarget, "unknown-user-file.txt"), System.Text.Encoding.UTF8) != "keep" ||
            store.State.Libraries.Any(l => l.Name == "UI temporary library")) throw new InvalidOperationException("Relocation/form output is incorrect.");
        Console.WriteLine("PASS: relocated ledger reopened; clear operated only on the new target; source, unknown files and original missing path retained; cancelled forms did not save.");
    }
}
