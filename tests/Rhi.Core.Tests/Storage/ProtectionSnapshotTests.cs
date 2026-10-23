using System.Text;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Storage;

public sealed class ProtectionSnapshotTests
{
    [Fact]
    public void Hiding_a_copy_is_a_persisted_display_preference_and_preserves_backup_files()
    {
        using var f = new Sandbox();
        DeployPair(f);
        File.WriteAllText(f.Target, "user edit", Encoding.UTF8);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Run(new(f.Library.Id, null, OperationKind.Clear))).Status);
        var copy = Assert.Single(ProtectionCatalog.Read(f.Store).Operations);
        var root = Path.Combine(f.Store.Root, "protection");
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, BlobStore.Hash);
        var receipts = AtomicJson.Fingerprint(f.Store.State.Receipts);
        f.Store.Save(f.Store.State with { Preferences = f.Store.State.Preferences with { HiddenProtectionIds = [copy.Entry.OperationId] } });
        var persisted = AtomicJson.Read<WorkspaceState>(Path.Combine(f.Store.Root, "workspace.json"));
        Assert.Contains(copy.Entry.OperationId, persisted.Preferences.HiddenProtectionIds!);
        Assert.Equal(receipts, AtomicJson.Fingerprint(persisted.Receipts));
        Assert.Equal(copy.Entry.OperationId, Assert.Single(ProtectionCatalog.Read(f.Store).Operations).Entry.OperationId);
        Assert.Equal(before.Count, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        foreach (var pair in before) Assert.Equal(pair.Value, BlobStore.Hash(pair.Key));
    }

    [Fact]
    public void Version_name_cannot_collide_with_the_description_file()
    {
        using var f = new Sandbox();
        var entry = new ProtectionEntry(Guid.NewGuid(), f.Game.Id, f.Library.Id, f.Game.DeploymentRoot,
            [], DateTimeOffset.UtcNow, f.Game.Name, f.Library.Name,
            VersionId: f.Version.Id, VersionLabel: "备份说明.txt", FullVersion: true);
        f.Store.Protect(entry);
        var copy = Assert.Single(ProtectionCatalog.Read(f.Store).Operations);
        Assert.Equal("_备份说明.txt", Path.GetFileName(copy.Directory));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(copy.Directory)!, "备份说明.txt")));
    }

    private static void DeployPair(Sandbox f)
    {
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(f.Source)!, "unchanged.dll"), "binary");
        var version = f.Version with { SourcePath = Path.GetDirectoryName(f.Source)!, Label = "ReShade_Setup_6.8.0_Addon-DX-64" };
        f.Store.Save(f.Store.State with { Libraries = [f.Library with { Versions = [version] }] });
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Deploy()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Snapshot_contains_entire_old_version_and_actual_target_contents(bool change)
    {
        using var f = new Sandbox();
        DeployPair(f);
        File.WriteAllText(f.Target, "user edit", Encoding.UTF8);
        File.WriteAllText(f.Source, "source edit must not enter copy", Encoding.UTF8);
        var next = f.AddVersion("v2");
        var result = Assert.Single(f.Run(new(f.Library.Id, change ? next.Id : null, change ? OperationKind.Change : OperationKind.Clear)));
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        var copy = Assert.Single(ProtectionCatalog.Read(f.Store).Operations);
        Assert.True(copy.Entry.FullVersion);
        Assert.Equal(f.Version.Id, copy.Entry.VersionId);
        Assert.Equal("ReShade_Setup_6.8.0_Addon-DX-64", copy.Entry.VersionLabel);
        Assert.Equal(copy.Entry.VersionLabel, Path.GetFileName(copy.Directory));
        Assert.Equal(f.Game.DeploymentRoot, copy.Entry.TargetRoot);
        Assert.Equal(2, copy.Entry.Files.Length);
        Assert.Equal("mod.txt", Assert.Single(copy.Entry.ModifiedFiles!));
        Assert.Equal("user edit", File.ReadAllText(Path.Combine(copy.Directory, "mod.txt"), Encoding.UTF8));
        Assert.Equal("binary", File.ReadAllText(Path.Combine(copy.Directory, "unchanged.dll"), Encoding.UTF8));
        var info = Path.Combine(Path.GetDirectoryName(copy.Directory)!, "备份说明.txt");
        Assert.True(File.Exists(info));
        Assert.False(File.Exists(Path.Combine(copy.Directory, "备份说明.txt")));
        Assert.True(File.ReadAllBytes(info).AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
    }

    [Fact]
    public void Missing_managed_file_is_recorded_and_never_replaced_from_source()
    {
        using var f = new Sandbox();
        DeployPair(f);
        File.Move(f.Target, f.Target + ".outside-receipt");
        var plan = f.Plan(new DeploymentRequest(f.Library.Id, null, OperationKind.Clear));
        Assert.True(Assert.Single(plan.Libraries).RequiresProtection);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Engine.Execute(plan, new(true, true))).Status);
        var copy = Assert.Single(ProtectionCatalog.Read(f.Store).Operations);
        Assert.Null(copy.Entry.Files.Single(x => x.RelativePath == "mod.txt").BeforeHash);
        Assert.False(File.Exists(Path.Combine(copy.Directory, "mod.txt")));
        Assert.True(File.Exists(Path.Combine(copy.Directory, "unchanged.dll")));
        Assert.True(File.Exists(f.Target + ".outside-receipt"));
    }

    [Fact]
    public void Direct_clear_requires_explicit_consent_and_restores_original_without_protection_copy()
    {
        using var f = new Sandbox();
        File.WriteAllText(f.Target, "original");
        f.Deploy();
        File.WriteAllText(f.Target, "user edit");
        var plan = f.Plan(new DeploymentRequest(f.Library.Id, null, OperationKind.Clear));
        Assert.Equal("ConsentRequired", Assert.Throws<DeploymentException>(() => f.Engine.Execute(plan, new(true, false))).Code);
        Assert.Equal("user edit", File.ReadAllText(f.Target));
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Engine.Execute(plan, new(true, false, true))).Status);
        Assert.Equal("original", File.ReadAllText(f.Target));
        Assert.Empty(ProtectionCatalog.Read(f.Store).Operations);
        Assert.Empty(f.Store.State.Receipts);
    }

    [Fact]
    public void Discard_consent_cannot_be_reused_for_version_changes()
    {
        using var f = new Sandbox();
        f.Deploy(); File.WriteAllText(f.Target, "manual");
        var next = f.AddVersion("v2");
        var plan = f.Plan(new DeploymentRequest(f.Library.Id, next.Id, OperationKind.Change));
        Assert.Equal("ConsentRequired", Assert.Throws<DeploymentException>(() => f.Engine.Execute(plan, new(true, false, true))).Code);
        Assert.Equal("manual", File.ReadAllText(f.Target));
    }

    [Fact]
    public void Direct_clear_failure_still_rolls_back_to_the_user_edit()
    {
        using var f = new Sandbox();
        f.Deploy(); File.WriteAllText(f.Target, "manual");
        var engine = new DeploymentEngine(f.Store, (phase, _) => { if (phase == "BeforeCommit") throw new IOException("fault"); });
        var result = Assert.Single(engine.Execute(f.Plan(new DeploymentRequest(f.Library.Id, null, OperationKind.Clear)), new(true, false, true)));
        Assert.Equal(OperationStatus.Restored, result.Status);
        Assert.Equal("manual", File.ReadAllText(f.Target));
        Assert.Single(f.Store.State.Receipts);
        Assert.Empty(ProtectionCatalog.Read(f.Store).Operations);
        Assert.Empty(f.Store.Pending());
    }

    [Fact]
    public void Description_failure_happens_before_any_game_file_is_changed()
    {
        using var f = new Sandbox();
        DeployPair(f); File.WriteAllText(f.Target, "manual");
        var engine = new DeploymentEngine(f.Store, protectionDescription: _ => throw new IOException("description unavailable"));
        Assert.Equal(OperationStatus.Restored, Assert.Single(engine.Execute(f.Plan(new DeploymentRequest(f.Library.Id, null, OperationKind.Clear)), new(true, true))).Status);
        Assert.Equal("manual", File.ReadAllText(f.Target));
        Assert.Equal("binary", File.ReadAllText(Path.Combine(f.Game.DeploymentRoot, "unchanged.dll")));
        Assert.Single(f.Store.State.Receipts);
    }
}
