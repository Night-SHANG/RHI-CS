using System.Text.Json;
using Rhi.App.Services;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.UiSmoke.Seed;

internal sealed record FeatureMetadata(string Root, Guid ReShadeId, Guid AlphaId, Guid BetaId,
    string AlphaSource, string BetaSource, string AlphaHash, string BetaHash, string UnknownFile);

internal static class FeatureFixture
{
    public static void Prepare(string metadataPath)
    {
        var pointer = Bootstrap.Read() ?? throw new InvalidOperationException("Missing CI fixture.");
        if (!PathPolicy.Within(pointer.Root, Path.Combine(Path.GetTempPath(), "RhiTests")))
            throw new InvalidOperationException("Not a CI fixture.");
        using var store = WorkspaceStore.Open(pointer.Root, expectedId: pointer.Id);
        var game = store.State.Games.Single();
        var reshade = store.State.Libraries.Single(l => l.Name == "ReShade");
        if (game.Name != "CI Game" || store.State.Receipts.Length > 0 || store.Pending().Length > 0 ||
            store.State.Libraries.Any(l => l.Name is not "ReShade" and not "Other library"))
            throw new InvalidOperationException("Unexpected fixture state.");
        var root = Directory.GetParent(store.Root)!.FullName;
        ResourceLibrary Library(string name)
        {
            var directory = Path.Combine(root, "feature-" + name);
            Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "shared.txt");
            File.WriteAllText(source, name, System.Text.Encoding.UTF8);
            return new(Guid.NewGuid(), name, "", [new(Guid.NewGuid(), "v1", source, null, ResourceKind.Files)]);
        }
        var alpha = Library("Alpha");
        var beta = Library("Beta");
        store.Save(store.State with { Libraries = [reshade, alpha, beta], Preferences = new("Light", "zh-CN") });
        var plan = new DeploymentPlanner(store).Build(store.State, game.Id,
            [new(reshade.Id, reshade.Versions.Single().Id, OperationKind.Deploy)]);
        if (new DeploymentEngine(store).Execute(plan, new(true, true)).Single().Status != OperationStatus.Succeeded)
            throw new InvalidOperationException("Cannot seed ReShade deployment.");
        File.AppendAllText(Path.Combine(game.DeploymentRoot, "ReShade.ini"), "\n[EXTERNAL]\nUserChange=preserve\n", System.Text.Encoding.UTF8);
        var unknown = Path.Combine(game.DeploymentRoot, "unknown-user-file.txt");
        File.WriteAllText(unknown, "keep", System.Text.Encoding.UTF8);
        var metadata = new FeatureMetadata(store.Root, reshade.Id, alpha.Id, beta.Id,
            alpha.Versions[0].SourcePath, beta.Versions[0].SourcePath,
            BlobStore.Hash(alpha.Versions[0].SourcePath)!, BlobStore.Hash(beta.Versions[0].SourcePath)!, unknown);
        using var output = new FileStream(metadataPath, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(output, metadata, AtomicJson.Options);
    }

    public static void Verify(string metadataPath)
    {
        var metadata = AtomicJson.Read<FeatureMetadata>(metadataPath);
        if (!PathPolicy.Within(metadata.Root, Path.Combine(Path.GetTempPath(), "RhiTests")))
            throw new InvalidOperationException("Not a CI fixture.");
        using var store = WorkspaceStore.Open(metadata.Root);
        if (store.State.Receipts.Length != 1 || store.State.Receipts[0].LibraryId != metadata.AlphaId ||
            store.State.Libraries.Any(l => l.Id == metadata.BetaId)) throw new InvalidOperationException("Unexpected final deployment/catalog state.");
        if (BlobStore.Hash(metadata.AlphaSource) != metadata.AlphaHash || BlobStore.Hash(metadata.BetaSource) != metadata.BetaHash ||
            File.ReadAllText(metadata.UnknownFile, System.Text.Encoding.UTF8) != "keep")
            throw new InvalidOperationException("Source or unknown target file changed.");
        var protectedCopy = ProtectionCatalog.Read(store).Operations.Single();
        if (protectedCopy.Entry.LibraryId != metadata.ReShadeId ||
            !File.ReadAllText(Path.Combine(protectedCopy.Directory, "ReShade.ini"), System.Text.Encoding.UTF8).Contains("UserChange=preserve"))
            throw new InvalidOperationException("External configuration was not protected.");
        Console.WriteLine("PASS: UI changes match persisted receipts; skipped library not deployed; removal retains sources; external edits protected; unknown files retained.");
    }
}
