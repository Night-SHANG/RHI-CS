using Rhi.Core.Models;

namespace Rhi.App.Services.Operations;

public sealed record TaskResource(Guid LibraryId, string Name, string Action, string Version);
public sealed record OperationContext(Guid GameId, string GameName, string TargetRoot, TaskResource[] Resources)
{
    public static OperationContext Capture(WorkspaceState state, Guid gameId, IEnumerable<DeploymentRequest> requests,
        bool recovery = false)
    {
        var game = state.Games.Single(g => g.Id == gameId);
        return new(gameId, game.Name, game.DeploymentRoot, requests.Select(r => new TaskResource(r.LibraryId,
            state.Libraries.Single(l => l.Id == r.LibraryId).Name, recovery ? "Recover" : r.Kind.ToString(),
            state.Libraries.Single(l => l.Id == r.LibraryId).Versions.FirstOrDefault(v => v.Id == r.VersionId)?.Label ??
                state.Receipts.FirstOrDefault(x => x.GameId == gameId && x.LibraryId == r.LibraryId)?.VersionLabel ?? "")).ToArray());
    }
}
