using System.Security.Cryptography;
using System.Text;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.UiSmoke.Seed;

// UI-only catalogue fixture. Runs after the independent deployment/restore verifiers.
internal static class ProtectionVisualFixture
{
    public static void Prepare(WorkspaceStore store)
    {
        var game = store.State.Games[0];
        var library = store.State.Libraries[0];
        var content = Encoding.UTF8.GetBytes("Disposable UI layout fixture");
        var hash = Convert.ToHexString(SHA256.HashData(content));
        store.Blobs.CaptureGenerated(content, hash);
        for (var i = 0; i < 24; i++)
        {
            store.Protect(new(Guid.NewGuid(), game.Id, library.Id, game.DeploymentRoot,
                [new("layout-fixture.txt", hash, hash)], DateTimeOffset.UtcNow.AddHours(-i),
                GameName: i % 2 == 0 ? "CI Game" : "CI Game B", LibraryName: library.Name,
                VersionId: Guid.NewGuid(), VersionLabel: "Visual version " + (i + 1),
                Kind: OperationKind.Clear, ModifiedFiles: [], FullVersion: true));
        }
        Console.WriteLine("Prepared 24 protected copies for bounded scrolling and section-layout checks.");
    }
}
