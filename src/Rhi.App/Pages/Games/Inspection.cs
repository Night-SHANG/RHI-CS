using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Models;

namespace Rhi.App.Pages;

public sealed partial class GamesView
{
    private async Task ViewDifferences(GameEntry game, ResourceLibrary library, DeploymentReceipt receipt)
    {
        var observation = await c.Session.InspectTargets(game.Id, refresh: false);
        var current = observation.Receipts.Single(r => r.LibraryId == library.Id);
        var panel = Ui.Stack(Ui.Text(game.Name + " · " + library.Name, 20), Ui.Text(receipt.TargetRoot),
            Ui.Label("InspectionOnly"));
        panel.Children.Add(Ui.FileDetails("Inspection", current.Files.Length, () => current.Files.Select(file =>
            file.RelativePath + " · " + Strings.Get(file.Health.ToString()))));
        foreach (var issue in current.Issues)
            panel.Children.Add(Ui.Text(Strings.Get(issue.Code) + "\n" + issue.Detail));
        var locked = c.Session.Store!.Pending().Any(j => j.GameId == game.Id);
        if (locked) panel.Children.Add(Ui.Label("NeedsRepair"));
        await c.Show("TargetDifferences", panel, stableContent: true);
    }
}
