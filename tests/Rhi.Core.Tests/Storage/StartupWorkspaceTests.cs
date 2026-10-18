using System.Text;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Storage;

public sealed class StartupWorkspaceTests
{
    [Fact]
    public void First_use_creates_Data_and_reopening_without_locator_preserves_identity()
    {
        using var f = new Sandbox();
        var app = Path.Combine(f.Root, "app"); Directory.CreateDirectory(app);
        Guid id;
        using (var store = StartupWorkspace.Open(app))
        { id = store.State.Id; Assert.Equal(Path.Combine(app, "Data"), store.Root); }
        using var reopened = StartupWorkspace.Open(app);
        Assert.Equal(id, reopened.State.Id);
    }

    [Fact]
    public void Existing_locator_wins_even_when_program_has_its_own_Data()
    {
        using var f = new Sandbox();
        var app = Path.Combine(f.Root, "app"); Directory.CreateDirectory(app);
        using (var separate = StartupWorkspace.Open(app)) Assert.NotEqual(f.Store.State.Id, separate.State.Id);
        var expected = f.Store.State.Id; f.Store.Dispose();
        using var store = StartupWorkspace.Open(app, f.Data, expected);
        Assert.Equal(f.Data, store.Root); Assert.Equal(expected, store.State.Id);
        Assert.Single(store.State.Games);
    }

    [Fact]
    public void Lost_previous_location_never_falls_back_to_empty_Data()
    {
        using var f = new Sandbox();
        var app = Path.Combine(f.Root, "app"); Directory.CreateDirectory(app);
        Assert.Throws<DeploymentException>(() => StartupWorkspace.Open(app, Path.Combine(f.Root, "lost"), Guid.NewGuid()));
        Assert.False(Directory.Exists(Path.Combine(app, "Data")));
    }

    [Fact]
    public void Nonempty_default_folder_is_not_claimed_or_modified()
    {
        using var f = new Sandbox();
        var app = Path.Combine(f.Root, "app");
        var data = Path.Combine(app, "Data"); Directory.CreateDirectory(data);
        var file = Path.Combine(data, "user.txt"); File.WriteAllText(file, "keep", Encoding.UTF8);
        Assert.Throws<DeploymentException>(() => StartupWorkspace.Open(app));
        Assert.Equal("keep", File.ReadAllText(file, Encoding.UTF8));
        Assert.Single(Directory.GetFileSystemEntries(data));
    }

    [Fact]
    public void Data_name_occupied_by_a_file_does_not_overwrite_it()
    {
        using var f = new Sandbox();
        var app = Path.Combine(f.Root, "app"); Directory.CreateDirectory(app);
        var file = Path.Combine(app, "Data"); File.WriteAllText(file, "keep", Encoding.UTF8);
        Assert.ThrowsAny<IOException>(() => StartupWorkspace.Open(app));
        Assert.Equal("keep", File.ReadAllText(file, Encoding.UTF8));
    }

    [Fact]
    public void Existing_default_data_with_source_overlap_is_rejected_without_reset()
    {
        using var f = new Sandbox();
        var app = Path.Combine(f.Root, "app"); Directory.CreateDirectory(app);
        byte[] ledger;
        using (var store = StartupWorkspace.Open(app))
        {
            store.Save(store.State with { Libraries = [f.Library with { Versions = [f.Version with { SourcePath = store.Root }] }] });
            ledger = File.ReadAllBytes(Path.Combine(store.Root, "workspace.json"));
        }
        Assert.Throws<DeploymentException>(() => StartupWorkspace.Open(app));
        Assert.Equal(ledger, File.ReadAllBytes(Path.Combine(app, "Data", "workspace.json")));
    }
}
