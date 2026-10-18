using System.Text;
using Rhi.App.Services;
using Rhi.Core.Deployment;
using Rhi.Core.Storage;
using Rhi.Core.Tests.ReShade;

namespace Rhi.UiSmoke.Seed;

public static class OfflineFixture
{
    public sealed record Metadata(string Root, string Installer, string Parent, string StateHash, string InputHash);
    public static void Prepare(string metadataPath)
    {
        var pointer = Bootstrap.Read() ?? throw new InvalidOperationException("Missing fixture.");
        if (!PathPolicy.Within(pointer.Root, Path.Combine(Path.GetTempPath(), "RhiTests"))) throw new InvalidOperationException("Not a CI fixture.");
        using var store = WorkspaceStore.Open(pointer.Root);
        var parent = Path.Combine(Path.GetDirectoryName(pointer.Root)!, "offline-output");
        Directory.CreateDirectory(parent);
        var installer = Path.Combine(parent, "ReShade-setup.exe");
        File.WriteAllBytes(installer, ReShadeFixture.Archive());
        AtomicJson.Write(metadataPath, new Metadata(pointer.Root, installer, parent, AtomicJson.Fingerprint(store.State), BlobStore.Hash(installer)!));
    }

    public static void Verify(string metadataPath)
    {
        var data = AtomicJson.Read<Metadata>(metadataPath);
        if (!PathPolicy.Within(data.Root, Path.Combine(Path.GetTempPath(), "RhiTests"))) throw new InvalidOperationException("Not a CI fixture.");
        using var store = WorkspaceStore.Open(data.Root);
        var output = Path.Combine(data.Parent, "UI-offline");
        if (!File.ReadAllBytes(Path.Combine(output, "dxgi.dll")).SequenceEqual(ReShadeFixture.Pe(true)) ||
            !File.ReadAllText(Path.Combine(output, "ReShade.ini"), Encoding.UTF8).Contains(@".\reshade-shaders\Shaders\**") ||
            BlobStore.Hash(data.Installer) != data.InputHash) throw new InvalidOperationException("Invalid offline export or modified installer.");
        // Visual tests change preferences, but must not implicitly add or deploy this source.
        if (store.State.Libraries.SelectMany(l => l.Versions).Any(v => PathPolicy.Within(v.SourcePath, output)))
            throw new InvalidOperationException("Export unexpectedly changed the resource catalog.");
        Console.WriteLine("PASS: offline UI exported the expected DLL and INI; installer preserved; no implicit library registration.");
    }
}
