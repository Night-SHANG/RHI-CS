using Rhi.App.Services;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.UiSmoke.Seed;

internal static class PendingConfigurationFixture
{
    public static void Prepare()
    {
        var pointer = Bootstrap.Read() ?? throw new InvalidOperationException("Missing isolated UI fixture.");
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "RhiTests");
        if (!PathPolicy.Within(pointer.Root, fixtureRoot)) throw new InvalidOperationException("Not a CI fixture path.");
        using var store = WorkspaceStore.Open(pointer.Root, expectedId: pointer.Id);
        var game = store.State.Games.Single();
        var library = store.State.Libraries.Single();
        if (game.Name != "CI Game" || library.Name != "ReShade" || store.State.Receipts.Length != 0 || store.Pending().Length != 0)
            throw new InvalidOperationException("Unexpected UI fixture state; refusing to change it.");
        store.Save(store.State with
        {
            Libraries = [library, new(Guid.NewGuid(), "Other library", "", [])],
            Preferences = new("Dark", "zh-CN")
        });
        // Model a persisted preparation interrupted before any target write, not a real crash test.
        store.WriteJournal(new(Guid.NewGuid(), store.State.Id, game.Id, library.Id, OperationKind.Deploy,
            game.DeploymentRoot, game.ExePath, BlobStore.Hash(game.ExePath)!, OperationStatus.Preparing,
            [], [], -1, null, DateTimeOffset.UtcNow, DirectoryLease.Inspect(game.DeploymentRoot), FileProtocol: 1));
        Console.WriteLine(store.Root);
    }
}
