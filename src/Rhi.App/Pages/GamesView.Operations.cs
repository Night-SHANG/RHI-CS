using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;
using Rhi.Core.Models;

namespace Rhi.App.Pages;

public sealed partial class GamesView
{
    private async Task Preflight(GameEntry game, DeploymentRequest[] requests)
    {
        if (WindowsIntegration.IsGameRunning(game.ExePath)) throw new DeploymentException("GameRunning");
        var plan = await c.Session.Preflight(game.Id, requests);
        var panel = Ui.Stack(Ui.Text(game.Name, 22), Ui.Text(game.DeploymentRoot), Ui.Label("NotWrittenYet"));
        foreach (var library in plan.Libraries)
        {
            panel.Children.Add(Ui.Text(library.LibraryName + " · " + library.VersionLabel + " · " + Strings.Get(library.Request.Kind.ToString()), 18));
            panel.Children.Add(Ui.Text(Strings.Format("PlanCounts", library.Changes.Count(f => f.BeforeHash is null && f.DesiredHash is not null),
                library.Changes.Count(f => f.BeforeHash is not null && f.DesiredHash is not null),
                library.Changes.Count(f => f.DesiredHash is null), library.Changes.Count(f => f.ExternalChange))));
            foreach (var file in library.Changes)
                panel.Children.Add(Ui.Text(file.RelativePath + " · " + Strings.Get(file.ExternalChange ? "ExternalChange" : file.DesiredHash is null ? "Remove" : "Write")));
        }
        foreach (var issue in plan.Issues) panel.Children.Add(Ui.Text(Strings.Get(issue.Code) + "\n" + issue.Detail));
        var overwrite = plan.Libraries.Any(l => l.RequiresOverwrite);
        var protect = plan.Libraries.Any(l => l.RequiresProtection);
        if (overwrite) panel.Children.Add(Ui.Label("OverwriteConsent"));
        if (protect) panel.Children.Add(Ui.Label("ProtectionConsent"));
        panel.Children.Add(Ui.Label("SkipLibraryHelp"));
        var action = protect ? "ProtectAndExecute" : overwrite ? "BackupAndExecute" : "ConfirmExecute";
        if (!await c.Confirm("Preflight", panel, action, plan.CanExecute)) return;
        if (WindowsIntegration.IsGameRunning(game.ExePath)) throw new DeploymentException("GameRunning");
        await c.Session.Execute(plan, new(overwrite, protect));
    }

    private async Task Recover(OperationJournal journal)
    {
        if (WindowsIntegration.IsGameRunning(journal.ExePath)) throw new DeploymentException("GameRunning");
        var panel = Ui.Stack(Ui.Label("RecoveryHelp"), Ui.Text(journal.TargetRoot),
            Ui.Text(string.Join("\n", journal.Files.Select(f => f.RelativePath))));
        if (await c.Confirm("CheckRecovery", panel, "Recover")) await c.Session.Recover(journal.Id);
    }
}
