using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.Core.Tests.Deployment;

internal sealed class Sandbox : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "RhiTests", Guid.NewGuid().ToString("N"));
    public string Data => Path.Combine(Root, "data");
    public string Source => Path.Combine(Root, "source", "mod.txt");
    public string Target => Path.Combine(Root, "game", "mod.txt");
    public WorkspaceStore Store { get; }
    public DeploymentEngine Engine { get; }
    public GameEntry Game { get; }
    public ResourceVersion Version { get; }
    public ResourceLibrary Library { get; }

    public Sandbox(Action<string, int>? fault = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        File.WriteAllText(Source, "v1");
        var exe = Path.Combine(Root, "game", "game.exe");
        File.WriteAllText(exe, "test executable identity");
        Game = new(Guid.NewGuid(), "测试游戏", Path.GetDirectoryName(exe)!, exe,
            Path.GetDirectoryName(exe)!, "DirectX 12", "x64", "Manual");
        Version = new(Guid.NewGuid(), "v1", Source, null, ResourceKind.Files);
        Library = new(Guid.NewGuid(), "测试库", "", [Version]);
        Store = WorkspaceStore.Open(Data, create: true);
        Store.Save(Store.State with { Games = [Game], Libraries = [Library] });
        Engine = new(Store, fault);
    }

    public ResourceVersion AddVersion(string text)
    {
        var folder = Path.Combine(Root, text);
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "mod.txt");
        File.WriteAllText(source, text);
        var next = new ResourceVersion(Guid.NewGuid(), text, source, null, ResourceKind.Files);
        Store.Save(Store.State with { Libraries = [Library with { Versions = [Version, next] }] });
        return next;
    }

    public BatchPlan Plan(params DeploymentRequest[] requests) =>
        new DeploymentPlanner(Store).Build(Store.State, Game.Id, requests);
    public IReadOnlyList<OperationResult> Run(DeploymentRequest request) =>
        Engine.Execute(Plan(request), new(true, true));
    public IReadOnlyList<OperationResult> Deploy() => Run(new(Library.Id, Version.Id, OperationKind.Deploy));
    // CI temporary directories are retained for diagnosis, never delete user paths.
    public void Dispose() => Store.Dispose();
}
