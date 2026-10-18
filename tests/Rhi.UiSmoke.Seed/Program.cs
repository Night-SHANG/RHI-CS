using Rhi.App.Services;
using Rhi.Core.Models;
using Rhi.Core.Tests.ReShade;

if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
    throw new InvalidOperationException("This fixture is restricted to disposable GitHub Actions runners.");
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
