using Rhi.Core.Configuration;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Configuration;

public sealed class LibraryRelocationTests
{
    [Fact]
    public void Existing_preferences_without_root_remain_readable()
    {
        var preferences = System.Text.Json.JsonSerializer.Deserialize<Preferences>("{\"Theme\":\"Dark\",\"Language\":\"en-US\"}", AtomicJson.Options)!;
        Assert.Equal("Dark", preferences.Theme);
        Assert.Equal("en-US", preferences.Language);
        Assert.Null(preferences.LibraryRoot);
    }

    private static ResourceLibrary Prepare(Sandbox box)
    {
        var library = box.Library with { SourceRoot = Path.GetDirectoryName(box.Source) };
        box.Store.Save(box.Store.State with { Libraries = [library] });
        return library;
    }

    [Fact]
    public void Changing_default_only_changes_browse_start_and_survives_reopen()
    {
        using var box = new Sandbox(); Prepare(box); box.Deploy();
        var before = box.Store.State;
        var other = Directory.CreateDirectory(Path.Combine(box.Root, "another-library-base")).FullName;
        var root = ConfigurationRules.LibraryDirectory(before, box.Data, other);
        box.Store.Save(before with { Preferences = before.Preferences with { LibraryRoot = root } });
        box.Store.Dispose();
        using var reopened = WorkspaceStore.Open(box.Data);
        Assert.Equal(root, reopened.State.Preferences.LibraryRoot);
        Assert.Equal(AtomicJson.Fingerprint(before.Libraries), AtomicJson.Fingerprint(reopened.State.Libraries));
        Assert.Equal(AtomicJson.Fingerprint(before.Receipts), AtomicJson.Fingerprint(reopened.State.Receipts));
        Assert.Equal("v1", File.ReadAllText(box.Source));
    }

    [Fact]
    public void Relocation_after_manual_move_preserves_ids_receipts_and_original_restore()
    {
        using var box = new Sandbox(); var library = Prepare(box);
        File.WriteAllText(box.Target, "original"); box.Deploy();
        var receipts = AtomicJson.Fingerprint(box.Store.State.Receipts);
        var moved = Path.Combine(box.Root, "moved-library");
        Directory.Move(library.SourceRoot!, moved);
        var plan = LibraryRelocation.Preview(box.Store.State, box.Data, library.Id, moved);
        Assert.Equal("RelocationMatched", Assert.Single(plan.Matches).Status);
        Assert.Equal(library.SourceRoot, box.Store.State.Libraries.Single().SourceRoot);
        box.Store.Save(LibraryRelocation.Apply(box.Store.State, box.Data, plan));
        var version = box.Store.State.Libraries.Single().Versions.Single();
        Assert.Equal(box.Version.Id, version.Id);
        Assert.Equal(Path.Combine(moved, "mod.txt"), version.SourcePath);
        Assert.Equal(receipts, AtomicJson.Fingerprint(box.Store.State.Receipts));
        Assert.False(Directory.Exists(library.SourceRoot));
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(box.Run(new(library.Id, null, OperationKind.Clear))).Status);
        Assert.Equal("original", File.ReadAllText(box.Target));
        Assert.Equal("v1", File.ReadAllText(version.SourcePath));
    }

    [Fact]
    public void Missing_and_outside_paths_are_reported_and_not_rewritten()
    {
        using var box = new Sandbox(); var library = Prepare(box);
        var outside = box.AddVersion("outside");
        library = library with { Versions = [box.Version, outside] };
        box.Store.Save(box.Store.State with { Libraries = [library] });
        var empty = Directory.CreateDirectory(Path.Combine(box.Root, "empty-new-root")).FullName;
        var plan = LibraryRelocation.Preview(box.Store.State, box.Data, library.Id, empty);
        Assert.Equal(new[] { "RelocationMissing", "RelocationOutsideRoot" }, plan.Matches.Select(m => m.Status));
        var updated = LibraryRelocation.Apply(box.Store.State, box.Data, plan);
        Assert.Equal(library.Versions, updated.Libraries.Single().Versions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stale_preview_is_rejected(bool editConfiguration)
    {
        using var box = new Sandbox(); var library = Prepare(box);
        var root = Directory.CreateDirectory(Path.Combine(box.Root, "new-root")).FullName;
        File.WriteAllText(Path.Combine(root, "mod.txt"), "copy");
        var plan = LibraryRelocation.Preview(box.Store.State, box.Data, library.Id, root);
        if (editConfiguration) box.Store.Save(box.Store.State with { Libraries = [library with { Name = "changed" }] });
        else File.Move(Path.Combine(root, "mod.txt"), Path.Combine(root, "mod.absent"));
        Assert.Equal("ChangedSincePreflight", Assert.Throws<DeploymentException>(() =>
            LibraryRelocation.Apply(box.Store.State, box.Data, plan)).Code);
        Assert.Equal(box.Source, box.Store.State.Libraries.Single().Versions.Single().SourcePath);
    }

    [Fact]
    public void Default_directory_rejects_Data_game_and_missing_directory()
    {
        using var box = new Sandbox();
        foreach (var path in new[] { box.Data, box.Game.DeploymentRoot, Path.Combine(box.Root, "missing") })
            Assert.Throws<DeploymentException>(() => ConfigurationRules.LibraryDirectory(box.Store.State, box.Data, path));
    }
}
