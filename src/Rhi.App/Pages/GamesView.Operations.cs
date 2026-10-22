using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;
using Rhi.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace Rhi.App.Pages;

public sealed partial class GamesView
{
    private async Task Preflight(GameEntry game, DeploymentRequest[] requests)
    {
        if (WindowsIntegration.IsGameRunning(game.ExePath)) throw new DeploymentException("GameRunning");
        var skipped = new HashSet<Guid>();
        while (true)
        {
        var active = requests.Where(r => !skipped.Contains(r.LibraryId)).ToArray();
        var plan = active.Length > 0 ? await c.Session.Preflight(game.Id, active) : null;
        var panel = Ui.Stack(Ui.Text(game.Name, 22), Ui.Text(game.DeploymentRoot), Ui.Label("NotWrittenYet"));
        foreach (var library in plan?.Libraries ?? [])
        {
            panel.Children.Add(Ui.Text(library.LibraryName + " · " + library.VersionLabel + " · " + Strings.Get(library.Request.Kind.ToString()), 18));
            panel.Children.Add(Ui.Text(Strings.Format("PlanCounts", library.Changes.Count(f => f.BeforeHash is null && f.DesiredHash is not null),
                library.Changes.Count(f => f.BeforeHash is not null && f.DesiredHash is not null && f.BeforeHash != f.DesiredHash),
                library.Changes.Count(f => f.DesiredHash is null && f.BeforeHash is not null), library.Changes.Count(f => f.ExternalChange))));
            panel.Children.Add(Ui.FileDetails(library.Request.LibraryId.ToString("N"), library.Changes.Length,
                () => library.Changes.Select(file => file.RelativePath + " · " + Strings.Get(file.ExternalChange ? "ExternalChange" :
                    file.BeforeHash == file.DesiredHash ? "NoFileChange" : file.DesiredHash is null ? "Remove" : "Write") +
                    string.Concat((file.ConfigurationEdits ?? []).Select(edit =>
                        $"\n[{edit.Section}] {edit.Key}\n{edit.Before ?? Strings.Get("NotSet")} → {edit.After}")))));
        }
        foreach (var issue in plan?.Issues ?? []) panel.Children.Add(Ui.Text(Strings.Get(issue.Code) + "\n" + issue.Detail));
        var overwrite = plan?.Libraries.Any(l => l.RequiresOverwrite) == true;
        var protect = plan?.Libraries.Any(l => l.RequiresProtection) == true;
        if (overwrite) panel.Children.Add(Ui.Label("OverwriteConsent"));
        if (protect) panel.Children.Add(Ui.Label("ProtectionConsent"));
        var reviewedSkip = new HashSet<Guid>(skipped);
        Action? updateAction = null;
        if (requests.Length > 1)
        {
            panel.Children.Add(Ui.Label("SkipLibraryHelp"));
            foreach (var request in requests)
            {
                var name = c.Session.State!.Libraries.Single(l => l.Id == request.LibraryId).Name;
                var check = new CheckBox { Content = Strings.Format("SkipLibrary", name), IsChecked = skipped.Contains(request.LibraryId) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(check, "SkipLibrary_" + request.LibraryId.ToString("N"));
                void Update() { if (check.IsChecked == true) reviewedSkip.Add(request.LibraryId); else reviewedSkip.Remove(request.LibraryId); updateAction?.Invoke(); }
                check.Checked += (_, _) => Update(); check.Unchecked += (_, _) => Update();
                panel.Children.Add(check);
                if (skipped.Contains(request.LibraryId)) panel.Children.Add(Ui.Text(name + " · " + Strings.Get("SkippedByUser")));
            }
        }
        var action = protect ? "ProtectAndExecute" : overwrite ? "BackupAndExecute" : "ConfirmExecute";
        var discard = false;
        if (protect && active.All(r => r.Kind == OperationKind.Clear))
        {
            panel.Children.Add(Ui.Label("DirectClearHelp"));
            var choice = await c.Choose("Preflight", panel, "ProtectAndClear", "DirectClear", configure: dialog =>
            {
                updateAction = () =>
                {
                    var changed = !skipped.SetEquals(reviewedSkip);
                    dialog.PrimaryButtonText = Strings.Get(changed ? "RecheckSelection" : "ProtectAndClear");
                    dialog.IsPrimaryButtonEnabled = changed || plan?.CanExecute == true;
                    dialog.IsSecondaryButtonEnabled = !changed && plan?.CanExecute == true;
                };
                updateAction();
            }, stableContent: true);
            if (choice == ContentDialogResult.None) return;
            discard = choice == ContentDialogResult.Secondary;
        }
        else if (!await c.Confirm("Preflight", panel, action, configure: dialog =>
        {
            updateAction = () =>
            {
                var changed = !skipped.SetEquals(reviewedSkip);
                dialog.SecondaryButtonText = Strings.Get(changed ? "RecheckSelection" : active.Length == 0 ? "FinishReview" : action);
                dialog.IsSecondaryButtonEnabled = changed || active.Length == 0 || plan?.CanExecute == true;
            };
            updateAction();
        }, stableContent: true)) return;
        if (!skipped.SetEquals(reviewedSkip)) { skipped = reviewedSkip; continue; }
        var omitted = requests.Where(r => skipped.Contains(r.LibraryId)).ToArray();
        if (active.Length == 0) { c.Session.RecordSkipped(game.Id, omitted); return; }
        if (WindowsIntegration.IsGameRunning(game.ExePath)) throw new DeploymentException("GameRunning");
        await c.Session.Execute(plan!, new(overwrite, protect && !discard, discard), omitted);
        return;
        }
    }

    private async Task Recover(OperationJournal journal)
    {
        if (WindowsIntegration.IsGameRunning(journal.ExePath)) throw new DeploymentException("GameRunning");
        var preview = await c.Session.InspectRecovery(journal.Id);
        var panel = Ui.Stack(Ui.Label("RecoveryHelp"), Ui.Text(journal.TargetRoot),
            Ui.Label("NotWrittenYet"));
        if (preview.AlreadyCommitted) panel.Children.Add(Ui.Label("CommittedJournalPending"));
        panel.Children.Add(Ui.FileDetails("Recovery", preview.Files.Length, () => preview.Files.Select(file =>
            file.RelativePath + " · " + Strings.Get(!file.RequiresRestore ? "NoFileChange" : file.BeforeHash is null ? "Remove" : "Recover"))));
        foreach (var issue in preview.Issues) panel.Children.Add(Ui.Text(Strings.Get(issue.Code) + "\n" + issue.Detail));
        if (await c.Confirm("CheckRecovery", panel, "Recover", preview.CanRecover, stableContent: true)) await c.Session.Recover(journal.Id);
    }
}
