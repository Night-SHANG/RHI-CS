using Rhi.App.Services;
using Rhi.Core.Models;
using Rhi.Core.Tests.ReShade;
using Rhi.UiSmoke.Seed;

if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
    throw new InvalidOperationException("This fixture is restricted to disposable GitHub Actions runners.");
if (args.FirstOrDefault() is "--offline" or "--verify-offline")
{
    if (args.Length != 2) throw new ArgumentException("Expected explicit fixture metadata path.");
    if (args[0] == "--offline") OfflineFixture.Prepare(args[1]); else OfflineFixture.Verify(args[1]);
    return;
}
if (args.FirstOrDefault() == "--visual")
{
    var pointer = Bootstrap.Read() ?? throw new InvalidOperationException("Missing CI fixture.");
    if (!Rhi.Core.Deployment.PathPolicy.Within(pointer.Root, Path.Combine(Path.GetTempPath(), "RhiTests")))
        throw new InvalidOperationException("Not a CI fixture.");
    using var store = Rhi.Core.Storage.WorkspaceStore.Open(pointer.Root, expectedId: pointer.Id);
    store.Save(store.State with { Preferences = new("Light", "zh-CN") });
    return;
}
if (args.FirstOrDefault() is "--relocation" or "--verify-relocation")
{
    if (args.Length != 2) throw new ArgumentException("Expected explicit fixture metadata path.");
    if (args[0] == "--relocation") RelocationFixture.Prepare(args[1]); else RelocationFixture.Verify(args[1]);
    return;
}
if (args.FirstOrDefault() is "--features" or "--verify-features")
{
    if (args.Length != 2) throw new ArgumentException("Expected explicit fixture metadata path.");
    if (args[0] == "--features") FeatureFixture.Prepare(args[1]); else FeatureFixture.Verify(args[1]);
    return;
}
if (args.FirstOrDefault() == "--pending-configuration")
{
    PendingConfigurationFixture.Prepare();
    return;
}
if (Bootstrap.Read() is not null)
    throw new InvalidOperationException("Refusing to replace an existing application locator.");
using var fixture = new ReShadeFixture();
fixture.Box.Store.Save(fixture.Box.Store.State with
{
    Games = [fixture.Box.Store.State.Games[0] with { Name = "CI Game" }],
    Libraries = [fixture.Box.Store.State.Libraries[0] with { Name = "ReShade" }],
    Preferences = new("Light", args.FirstOrDefault() ?? "zh-CN")
});
Bootstrap.Save(new(fixture.Box.Store.Root, fixture.Box.Store.State.Id));
Console.WriteLine(fixture.Box.Store.Root);
