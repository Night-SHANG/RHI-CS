using System.Globalization;
using System.Text;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.Storage;

public sealed class ProtectionLayoutTests
{
    [Fact]
    public void New_copies_preserve_relative_paths_and_original_restore_after_reopen_and_rename()
    {
        using var f = new Sandbox();
        f.Store.Save(f.Store.State with { Libraries = [f.Library with { TargetRelative = "presets" }] });
        var target = Path.Combine(f.Game.DeploymentRoot, "presets", "mod.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "original", Encoding.UTF8);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Deploy()).Status);
        File.WriteAllText(target, "user edit", Encoding.UTF8);
        var result = Assert.Single(f.Run(new(f.Library.Id, null, OperationKind.Clear)));
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal("original", File.ReadAllText(target, Encoding.UTF8));
        Assert.Equal("v1", File.ReadAllText(f.Source, Encoding.UTF8));
        var copy = Assert.Single(ProtectionCatalog.Read(f.Store).Operations);
        Assert.Equal(copy.Directory, ProtectionCatalog.Find(f.Store, copy.Entry.OperationId)!.Directory);
        var relative = Assert.IsType<string>(copy.Entry.ProtectionDirectory);
        var parts = relative.Split(Path.DirectorySeparatorChar);
        Assert.Equal("测试游戏", parts[0]);
        Assert.Equal("测试库", parts[1]);
        Assert.StartsWith(copy.Entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss_zzz", CultureInfo.InvariantCulture)
            .Replace(':', '-'), parts[2]);
        Assert.EndsWith("__" + result.OperationId!.Value.ToString("N"), parts[2]);
        Assert.Equal("user edit", File.ReadAllText(Path.Combine(copy.Directory, "presets", "mod.txt"), Encoding.UTF8));
        f.Store.Save(f.Store.State with { Games = [f.Game with { Name = "renamed" }], Libraries = [] });
        f.Store.Dispose();
        using var reopened = WorkspaceStore.Open(f.Data);
        var after = Assert.Single(ProtectionCatalog.Read(reopened).Operations);
        Assert.Equal(copy.Directory, after.Directory);
        Assert.Equal("测试游戏", after.Entry.GameName);
        Assert.Equal("测试库", after.Entry.LibraryName);
        Assert.Empty(reopened.Pending());
    }

    [Fact]
    public void Legacy_json_without_layout_and_new_copies_are_read_without_rewriting_legacy_data()
    {
        using var f = new Sandbox();
        var id = Guid.NewGuid();
        var root = Path.Combine(f.Data, "protection");
        var legacyFolder = Path.Combine(root, id.ToString("N"), "files");
        Directory.CreateDirectory(legacyFolder);
        var legacyFile = Path.Combine(legacyFolder, "old.ini");
        File.WriteAllText(legacyFile, "old edit", Encoding.UTF8);
        var json = Path.Combine(root, id.ToString("N") + ".json");
        // Exact pre-layout shape: the new property is absent, rather than merely null.
        AtomicJson.Write(json, new { OperationId = id, GameId = f.Game.Id, LibraryId = f.Library.Id,
            TargetRoot = f.Game.DeploymentRoot, Files = new[] { new JournalFile("old.ini", BlobStore.Hash(legacyFile), null) },
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), GameName = "旧游戏", LibraryName = "旧资源库" });
        var before = File.ReadAllBytes(json);
        f.Deploy();
        File.WriteAllText(f.Target, "new edit", Encoding.UTF8);
        f.Run(new(f.Library.Id, null, OperationKind.Clear));
        var inventory = ProtectionCatalog.Read(f.Store);
        Assert.Equal(2, inventory.Operations.Length);
        var legacy = Assert.Single(inventory.Operations, x => x.Entry.OperationId == id);
        Assert.Null(legacy.Entry.ProtectionDirectory);
        Assert.Equal(legacy.Directory, ProtectionCatalog.Find(f.Store, id)!.Directory);
        Assert.Equal(legacyFolder, legacy.Directory);
        Assert.Equal(before, File.ReadAllBytes(json));
        Assert.Equal("old edit", File.ReadAllText(legacyFile, Encoding.UTF8));
        Assert.Equal("new edit", File.ReadAllText(Path.Combine(inventory.Operations[0].Directory, "mod.txt"), Encoding.UTF8));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("com1.ini", "_com1.ini")]
    [InlineData("LPT²", "_LPT²")]
    [InlineData(" 游戏:名/子目录? ", "游戏_名_子目录_")]
    [InlineData("..", null)]
    [InlineData("", null)]
    [InlineData("中文😀", "中文😀")]
    public void Labels_are_readable_safe_and_same_time_copies_do_not_collide(string name, string? expected)
    {
        using var f = new Sandbox();
        var hash = BlobStore.Hash(f.Source)!;
        f.Store.Blobs.Capture(f.Source, hash);
        var first = new ProtectionEntry(Guid.NewGuid(), f.Game.Id, f.Library.Id, f.Game.DeploymentRoot,
            [new("mod.txt", hash, null)], DateTimeOffset.UtcNow, name, name);
        var second = first with { OperationId = Guid.NewGuid() };
        f.Store.Protect(first);
        f.Store.Protect(second);
        var copies = ProtectionCatalog.Read(f.Store).Operations;
        Assert.Equal(2, copies.Length);
        Assert.NotEqual(copies[0].Directory, copies[1].Directory);
        foreach (var copy in copies)
        {
            var relative = copy.Entry.ProtectionDirectory!;
            Assert.Equal(expected ?? f.Game.Id.ToString("N"), relative.Split(Path.DirectorySeparatorChar)[0]);
            Assert.Equal(relative, PathPolicy.Relative(relative));
            Assert.Equal("v1", File.ReadAllText(Path.Combine(copy.Directory, "mod.txt"), Encoding.UTF8));
        }
    }

    [Fact]
    public void Long_names_are_bounded_without_cutting_a_surrogate_pair()
    {
        var entry = new ProtectionEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "unused", [],
            DateTimeOffset.UtcNow, new string('中', 39) + "😀more", new string('文', 100));
        var parts = ProtectionLayout.CreateDirectory(entry).Split(Path.DirectorySeparatorChar);
        Assert.Equal(new string('中', 39), parts[0]);
        Assert.Equal(40, parts[1].Length);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:\\outside")]
    [InlineData("")]
    [InlineData("game/library/wrong-operation")]
    public void Invalid_record_paths_cannot_redirect_copy_lookup(string directory)
    {
        using var f = new Sandbox();
        var entry = new ProtectionEntry(Guid.NewGuid(), f.Game.Id, f.Library.Id, f.Game.DeploymentRoot, [],
            DateTimeOffset.UtcNow, "game", "library", directory);
        AtomicJson.Write(Path.Combine(f.Data, "protection", entry.OperationId.ToString("N") + ".json"), entry);
        Assert.Throws<DeploymentException>(() => ProtectionCatalog.Read(f.Store));
        Assert.Equal("v1", File.ReadAllText(f.Source, Encoding.UTF8));
    }

    [Fact]
    public void Failure_after_protection_restores_user_edit_and_keeps_browsable_copy()
    {
        using var f = new Sandbox();
        f.Deploy();
        File.WriteAllText(f.Target, "manual", Encoding.UTF8);
        var next = f.AddVersion("v2");
        var engine = new DeploymentEngine(f.Store, (phase, _) =>
        { if (phase == "BeforeCommit") throw new IOException("injected"); });
        var result = Assert.Single(engine.Execute(f.Plan(new DeploymentRequest(f.Library.Id, next.Id, OperationKind.Change)), new(true, true)));
        Assert.Equal(OperationStatus.Restored, result.Status);
        Assert.Equal("manual", File.ReadAllText(f.Target, Encoding.UTF8));
        Assert.Equal(f.Version.Id, Assert.Single(f.Store.State.Receipts).VersionId);
        var copy = Assert.Single(ProtectionCatalog.Read(f.Store).Operations);
        Assert.NotNull(copy.Entry.ProtectionDirectory);
        Assert.Equal("manual", File.ReadAllText(Path.Combine(copy.Directory, "mod.txt"), Encoding.UTF8));
        Assert.Empty(f.Store.Pending());
    }
}
