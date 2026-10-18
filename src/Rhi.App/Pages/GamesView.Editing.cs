using Microsoft.UI.Xaml.Controls;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;
using Rhi.App.Services.Discovery;
using Rhi.Core.Deployment;
using Rhi.Core.Discovery;
using Rhi.Core.Models;
using Rhi.Core.Configuration;
using Microsoft.UI.Xaml.Automation;

namespace Rhi.App.Pages;

public sealed partial class GamesView
{
    private async Task Scan()
    {
        var found = await c.Session.ScanGames();
        var list = new ListView { ItemsSource = found.Games, DisplayMemberPath = "Name", MaxHeight = 260 };
        var panel = Ui.Stack(Ui.Label("SteamScanScope"), list, Ui.Text(string.Join("\n", found.Warnings)));
        if (found.Games.Length == 0) panel.Children.Add(Ui.Label("NoGames"));
        if (await c.Confirm("DiscoveredGames", panel, "SelectExecutable", false, dialog =>
            list.SelectionChanged += (_, _) => dialog.IsSecondaryButtonEnabled = list.SelectedItem is DiscoveredGame) && list.SelectedItem is DiscoveredGame game)
            await EditGame(discovered: game);
    }

    private async Task EditGame(GameEntry? existing = null, DiscoveredGame? discovered = null)
    {
        var name = Ui.Input("GameName", existing?.Name ?? discovered?.Name ?? "");
        var exe = Ui.Input("Executable", existing?.ExePath ?? "");
        var install = Ui.Input("InstallRoot", existing?.InstallRoot ?? discovered?.InstallRoot ?? "");
        var target = Ui.Input("DeploymentRoot", existing?.DeploymentRoot ?? discovered?.SuggestedDeploymentRoot ?? "");
        var apiNames = new[] { "Unknown", "DirectX 9", "DirectX 10/11/12", "OpenGL", "Vulkan" };
        var api = Ui.Choice("GraphicsApi", apiNames.Select(Strings.Get), Math.Max(0, Array.IndexOf(apiNames, existing?.Api)));
        AutomationProperties.SetAutomationId(name, "GameNameInput");
        AutomationProperties.SetAutomationId(exe, "GameExeInput");
        AutomationProperties.SetAutomationId(install, "GameInstallInput");
        AutomationProperties.SetAutomationId(target, "GameTargetInput");
        var pick = c.Button("SelectExecutable", async () =>
        {
            var path = await c.Windows.PickFile(".exe");
            if (path is null) return;
            exe.Text = path;
            target.Text = Path.GetDirectoryName(path)!;
            if (string.IsNullOrWhiteSpace(install.Text)) install.Text = target.Text;
            if (string.IsNullOrWhiteSpace(name.Text)) name.Text = Path.GetFileNameWithoutExtension(path);
            var suggestedApi = await Task.Run(() => GraphicsInspection.SuggestApi(path));
            api.SelectedIndex = Math.Max(0, Array.IndexOf(apiNames, suggestedApi));
        });
        var managed = existing is not null && c.Session.State!.Receipts.Any(r => r.GameId == existing.Id);
        exe.IsReadOnly = install.IsReadOnly = target.IsReadOnly = managed;
        pick.IsEnabled = !managed;
        var id = existing?.Id ?? Guid.NewGuid();
        GameEntry Read() => ConfigurationRules.Game(c.Session.State!, c.Session.Store!.Root,
            new(id, name.Text, install.Text, exe.Text, target.Text, apiNames[Math.Max(0, api.SelectedIndex)], "Unknown",
                existing?.Source ?? discovered?.Source ?? "Manual"));
        await c.Edit(existing is null ? "AddGame" : "Edit",
            Ui.Stack(name, exe, pick, install, target, api, Ui.Label(managed ? "ManagedPathHelp" : "ExeHelp")),
            () => { Read(); }, () =>
            {
                var game = Read(); var state = c.Session.State!;
                c.Session.Save(state with { Games = state.Games.Where(g => g.Id != game.Id).Append(game).ToArray() });
                c.GameId = game.Id;
                c.Refresh();
                return Task.CompletedTask;
            }, [name, exe, install, target, api, pick]);
    }

    private async Task ChooseVersion(GameEntry game, ResourceLibrary library, DeploymentReceipt receipt)
    {
        var versions = library.Versions.Where(v => v.Id != receipt.VersionId && SourceAvailable(v)).ToArray();
        var box = Ui.Choice("CandidateVersion", versions.Select(ResourcePresentation.VersionName));
        if (await c.Confirm("ChangeVersion", Ui.Stack(Ui.Text(Strings.Get("CurrentVersion") + ": " + receipt.VersionLabel), box), "Preflight", versions.Length > 0))
            await Preflight(game, [new(library.Id, versions[box.SelectedIndex].Id, OperationKind.Change)]);
    }
}
