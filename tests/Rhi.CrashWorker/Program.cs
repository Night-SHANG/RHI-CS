using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

// CI only: terminate a separate process without executing finally/rollback handlers.
using var store = WorkspaceStore.Open(args[0]);
var game = store.State.Games.Single();
var library = store.State.Libraries.Single();
var plan = new DeploymentPlanner(store).Build(store.State, game.Id,
    [new(library.Id, library.Versions[0].Id, OperationKind.Deploy)]);
var engine = new DeploymentEngine(store, (phase, _) =>
{
    if (phase == args[1]) Environment.Exit(97);
});
var results = engine.Execute(plan, new(true, true));
Console.Error.WriteLine(string.Join("; ", results.Select(r => $"{r.Status}: {r.Detail}")));
return 2; // The requested termination point was not reached.
