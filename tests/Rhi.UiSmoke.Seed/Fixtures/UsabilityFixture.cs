using Rhi.App.Services;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Tests.ReShade;

namespace Rhi.UiSmoke.Seed;

internal sealed record UsabilityMetadata(string Data, string BaseRoot, string OtherRoot, string LibraryRoot,
    string MovedRoot, string Source, Guid LibraryId, Guid VersionId, string GameRoot, string SourceHash);

internal static class UsabilityFixture
{
    public static void Prepare(string metadataPath)
    {
        using var fixture = new ReShadeFixture();
        var box = fixture.Box;
        var root = Directory.CreateDirectory(Path.Combine(box.Root, "Libraries")).FullName;
        var libraryRoot = Directory.CreateDirectory(Path.Combine(root, "Shader library")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(libraryRoot, "6.8.0")).FullName;
        for (var i = 0; i < 990; i++) File.WriteAllText(Path.Combine(source, $"shader-{i:D4}.fx"), "fixture " + i);
        var other = Directory.CreateDirectory(Path.Combine(box.Root, "OtherLibraries")).FullName;
        var moved = Directory.CreateDirectory(Path.Combine(other, "Shader library")).FullName;
        var movedSource = Directory.CreateDirectory(Path.Combine(moved, "6.8.0")).FullName;
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(movedSource, Path.GetFileName(file)));
        var version = new ResourceVersion(Guid.NewGuid(), "6.8.0", source, null, ResourceKind.Files);
        var library = new ResourceLibrary(Guid.NewGuid(), "Shader library", "shaders", [version], libraryRoot);
        box.Store.Save(box.Store.State with { Libraries = [library], Preferences = new("Light", "zh-CN") });
        Bootstrap.Save(new(box.Data, box.Store.State.Id));
        AtomicJson.Write(Path.GetFullPath(metadataPath), new UsabilityMetadata(box.Data, root, other, libraryRoot,
            moved, source, library.Id, version.Id, box.Game.DeploymentRoot, BlobStore.Hash(Path.Combine(source, "shader-0000.fx"))!));
    }

    public static void Verify(string metadataPath)
    {
        var data = AtomicJson.Read<UsabilityMetadata>(metadataPath);
        if (!PathPolicy.Within(data.Data, Path.Combine(Path.GetTempPath(), "RhiTests"))) throw new InvalidOperationException("Not a fixture.");
        using var store = WorkspaceStore.Open(data.Data);
        var library = store.State.Libraries.Single();
        var version = library.Versions.Single();
        if (store.State.Preferences.LibraryRoot != data.OtherRoot || library.SourceRoot != data.MovedRoot ||
            version.Id != data.VersionId || version.Label != "6.8.0 renamed" || version.SourcePath != Path.Combine(data.MovedRoot, "6.8.0") ||
            store.State.Receipts.Length != 0 || File.Exists(Path.Combine(data.GameRoot, "shaders", "shader-0000.fx")) ||
            BlobStore.Hash(Path.Combine(data.Source, "shader-0000.fx")) != data.SourceHash ||
            Directory.GetFiles(data.Source).Length != 990 || Directory.GetFiles(version.SourcePath).Length != 990)
            throw new InvalidOperationException("Usability saved configuration/source boundaries failed.");
        Console.WriteLine("PASS: preferences and version rename persisted; relocation updated only matched config; source files untouched; cancelled preflight did not deploy.");
    }
}
