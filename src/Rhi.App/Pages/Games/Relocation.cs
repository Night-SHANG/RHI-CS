using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Configuration;
using Rhi.Core.Models;

namespace Rhi.App.Pages;

public sealed partial class GamesView
{
    private async Task Relocate(GameEntry game)
    {
        var exe = Ui.Input("Executable");
        var install = Ui.Input("InstallRoot");
        var target = Ui.Input("DeploymentRoot");
        AutomationProperties.SetAutomationId(exe, "RelocationExe");
        AutomationProperties.SetAutomationId(install, "RelocationInstall");
        AutomationProperties.SetAutomationId(target, "RelocationTarget");
        var pick = c.Button("SelectExecutable", async () =>
        {
            var path = await c.Windows.PickFile(".exe");
            if (path is null) return;
            exe.Text = path;
            // Preserve the original EXE depth; the user can inspect/correct both paths.
            var relative = Path.GetRelativePath(game.InstallRoot, game.ExePath);
            var root = path;
            foreach (var part in relative.Split(Path.DirectorySeparatorChar)) root = Path.GetDirectoryName(root)!;
            install.Text = root;
            target.Text = Path.GetFullPath(Path.Combine(root, Path.GetRelativePath(game.InstallRoot, game.DeploymentRoot)));
        });
        RelocationPlan? plan = null;
        void Validate() => ConfigurationRules.Game(c.Session.State!, c.Session.Store!.Root,
            game with { ExePath = exe.Text, InstallRoot = install.Text, DeploymentRoot = target.Text }, relocation: true);
        var form = Ui.Stack(Ui.Label("RelocationHelp"), Ui.Text(Strings.Get("OldLocation") + ": " + game.DeploymentRoot),
            exe, pick, install, target);
        if (!await c.Edit("RelocateGame", form, Validate, async () =>
        {
            plan = await c.Session.InspectRelocation(game.Id, exe.Text, install.Text, target.Text);
            if (!plan.CanApply) throw new DeploymentException(plan.Issues.FirstOrDefault()?.Code ?? "InspectionFailed");
        }, [exe, install, target, pick], "CheckLocation")) return;
        var preview = Ui.Stack(Ui.Label("RelocationConfirmHelp"),
            Ui.Text(Strings.Get("OldLocation") + ": " + plan!.Original.DeploymentRoot),
            Ui.Text(Strings.Get("NewLocation") + ": " + plan.Candidate.DeploymentRoot),
            Ui.Text(Strings.Get("Executable") + ": " + plan.Candidate.ExePath),
            Ui.Text(Strings.Format("RelocationCounts", plan.Libraries, plan.Files)));
        if (await c.Confirm("RelocateGame", preview, "ConfirmLocation"))
            await c.Session.ApplyRelocation(plan);
    }
}
