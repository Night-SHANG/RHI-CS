using Microsoft.UI.Xaml.Controls;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Models;

namespace Rhi.App.Pages;

public sealed partial class GamesView
{
    private async Task ViewDifferences(GameEntry game, ResourceLibrary library, DeploymentReceipt receipt)
    {
        var observation = await c.Session.InspectTargets(game.Id);
        var current = observation.Receipts.Single(r => r.LibraryId == library.Id);
        var panel = Ui.Stack(Ui.Text(game.Name + " · " + library.Name, 20), Ui.Text(receipt.TargetRoot),
            Ui.Label("InspectionOnly"));
        foreach (var file in current.Files)
            panel.Children.Add(Ui.Text(file.RelativePath + " · " + Strings.Get(file.Health.ToString())));
        foreach (var issue in current.Issues)
            panel.Children.Add(Ui.Text(Strings.Get(issue.Code) + "\n" + issue.Detail));
        var actions = library.Versions.Any(v => v.Id != receipt.VersionId && SourceAvailable(v))
            ? new[] { "ChangeVersion", "ClearDeployment" } : new[] { "ClearDeployment" };
        var action = Ui.Choice("NextAction", actions.Select(Strings.Get));
        panel.Children.Add(action);
        var locked = c.Session.Store!.Pending().Any(j => j.GameId == game.Id);
        if (locked) panel.Children.Add(Ui.Label("NeedsRepair"));
        if (!await c.Confirm("TargetDifferences", panel, "Preflight", current.Issues.Length == 0 && !locked)) return;
        if (actions[action.SelectedIndex] == "ChangeVersion") await ChooseVersion(game, library, receipt);
        else await Preflight(game, [new(library.Id, null, OperationKind.Clear)]);
    }
}
