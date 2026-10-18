using System.Text.Json;
using Rhi.Core.Configuration;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Configuration;

public sealed class LibraryFolderTests
{
    [Fact]
    public void Old_library_without_root_remains_readable()
    {
        var json = "{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Old\",\"TargetRelative\":\"mods\",\"Versions\":[]}";
        var library = JsonSerializer.Deserialize<ResourceLibrary>(json, AtomicJson.Options)!;
        Assert.Null(library.SourceRoot);
        Assert.Equal("mods", library.TargetRelative);
    }

    [Fact]
    public void Library_folder_does_not_rewrite_or_move_existing_version_sources()
    {
        using var box = new Sandbox();
        var root = Path.Combine(Path.GetTempPath(), "RhiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var original = box.Version.SourcePath;
        var hash = BlobStore.Hash(original);
        var library = ConfigurationRules.Library(box.Store.State, box.Data,
            new(Guid.NewGuid(), "Group", "mods", [box.Version], root));
        Assert.Equal(original, library.Versions[0].SourcePath);
        Assert.Equal(hash, BlobStore.Hash(original));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        box.Store.Save(box.Store.State with { Libraries = box.Store.State.Libraries.Append(library).ToArray() });
        Assert.Equal(root, box.Store.State.Libraries.Single(l => l.Id == library.Id).SourceRoot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_folder_is_optional(string root)
    {
        using var box = new Sandbox();
        Assert.Null(ConfigurationRules.Library(box.Store.State, box.Data,
            new(Guid.NewGuid(), "Group", "", [], root)).SourceRoot);
    }

    [Fact]
    public void Data_folder_and_missing_folder_are_rejected_without_creation()
    {
        using var box = new Sandbox();
        ResourceLibrary Library(string path) => new(Guid.NewGuid(), "Group", "", [], path);
        Assert.Equal("SourceOverlapsData", Assert.Throws<DeploymentException>(() =>
            ConfigurationRules.Library(box.Store.State, box.Data, Library(box.Data))).Code);
        var missing = Path.Combine(box.Data, "missing-group");
        Assert.Equal("LibraryFolderMissing", Assert.Throws<DeploymentException>(() =>
            ConfigurationRules.Library(box.Store.State, box.Data, Library(missing))).Code);
        Assert.False(Directory.Exists(missing));
    }
}
