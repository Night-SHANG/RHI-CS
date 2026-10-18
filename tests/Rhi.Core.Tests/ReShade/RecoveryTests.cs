using System.Text;
using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Tests.ReShade;

public sealed class RecoveryTests
{
    [Fact]
    public void Change_then_source_loss_then_clear_preserves_original_and_protects_user_configuration()
    {
        using var f = new ReShadeFixture(installer: true);
        const string original = "[GENERAL]\nPresetPath=original.ini";
        File.WriteAllText(f.Config, original, Encoding.UTF8);
        var originalBytes = File.ReadAllBytes(f.Config);
        var originalDll = Encoding.UTF8.GetBytes("original proxy");
        var proxy = Path.Combine(f.Box.Game.DeploymentRoot, "dxgi.dll");
        File.WriteAllBytes(proxy, originalDll);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Box.Run(f.Deploy)).Status);
        File.AppendAllText(f.Config, "\n[CUSTOM]\nUserEdit=preserve", Encoding.UTF8);
        var edited = File.ReadAllBytes(f.Config);
        var version = f.Version with { Id = Guid.NewGuid(), Label = "Addon", Kind = ResourceKind.ReShadeAddon };
        var library = f.Box.Store.State.Libraries.Single();
        f.Box.Store.Save(f.Box.Store.State with { Libraries = [library with { Versions = [f.Version, version] }] });
        var change = f.Box.Plan(new DeploymentRequest(library.Id, version.Id, OperationKind.Change));
        Assert.True(change.CanExecute);
        Assert.True(change.Libraries[0].RequiresProtection);
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Box.Engine.Execute(change, new(true, true))).Status);
        Assert.Equal(edited, File.ReadAllBytes(f.Config));
        Assert.Contains(Directory.GetFiles(Path.Combine(f.Box.Data, "protection"), "ReShade.ini", SearchOption.AllDirectories),
            path => File.ReadAllBytes(path).SequenceEqual(edited));
        // Disconnect by changing the configured source to an absent path; do not delete fixture files.
        var missing = Path.Combine(Path.GetDirectoryName(f.Source)!, "offline.exe");
        f.Box.Store.Save(f.Box.Store.State with { Libraries = [library with
            { Versions = [f.Version with { SourcePath = missing }, version with { SourcePath = missing }] }] });
        Assert.Equal(OperationStatus.Succeeded, Assert.Single(f.Box.Run(f.Clear)).Status);
        Assert.Equal(originalBytes, File.ReadAllBytes(f.Config));
        Assert.Equal(originalDll, File.ReadAllBytes(proxy));
        Assert.Empty(f.Box.Store.State.Receipts);
    }

    [Fact]
    public void Failure_after_both_generated_files_restores_exact_before_state()
    {
        using var f = new ReShadeFixture(fault: (phase, index) =>
        { if (phase == "AfterWrite" && index == 1) throw new IOException("injected"); });
        File.WriteAllText(f.Config, "[GENERAL]\nPresetPath=before.ini", Encoding.Unicode);
        var before = File.ReadAllBytes(f.Config);
        var result = Assert.Single(f.Box.Run(f.Deploy));
        Assert.Equal(OperationStatus.Restored, result.Status);
        Assert.Equal(before, File.ReadAllBytes(f.Config));
        Assert.False(File.Exists(Path.Combine(f.Box.Game.DeploymentRoot, "dxgi.dll")));
        Assert.Empty(f.Box.Store.State.Receipts);
        Assert.Empty(f.Box.Store.Pending());
    }

    [Fact]
    public void Failed_recovery_retains_unknown_external_configuration_and_locks_further_writes()
    {
        using var f = new ReShadeFixture();
        var engine = new DeploymentEngine(f.Box.Store, (phase, index) =>
        {
            if (phase != "AfterWrite" || index != 1) return;
            File.WriteAllText(f.Config, "external-after-write", Encoding.UTF8);
            throw new IOException("injected");
        });
        var result = Assert.Single(engine.Execute(f.Box.Plan(f.Deploy), new(true, true)));
        Assert.Equal(OperationStatus.NeedsRepair, result.Status);
        Assert.Equal("external-after-write", File.ReadAllText(f.Config, Encoding.UTF8));
        Assert.Single(f.Box.Store.Pending());
        Assert.False(f.Box.Plan(f.Deploy).CanExecute);
    }
}
