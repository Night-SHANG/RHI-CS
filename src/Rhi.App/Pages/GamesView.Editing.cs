using Microsoft.UI.Xaml.Controls;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;
using Rhi.App.Services.Discovery;
using Rhi.Core.Deployment;
using Rhi.Core.Discovery;
using Rhi.Core.Models;

namespace Rhi.App.Pages;

public sealed partial class GamesView
{
    private async Task Scan()
    {
        var found = await Task.Run(RhiGameDiscovery.Scan);
        var list = new ListView { ItemsSource = found.Games, DisplayMemberPath = "Name", MaxHeight = 260 };
        var panel = Ui.Stack(Ui.Label("SteamScanScope"), list, Ui.Text(string.Join("\n", found.Warnings)));
        if (found.Games.Length == 0) panel.Children.Add(Ui.Label("NoGames"));
        if (await c.Confirm("DiscoveredGames", panel, "SelectExecutable", found.Games.Length > 0) && list.SelectedItem is DiscoveredGame game)
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
        if (!await c.Confirm(existing is null ? "AddGame" : "Edit", Ui.Stack(name, exe, pick, install, target, api, Ui.Label("ExeHelp")))) return;
        if (string.IsNullOrWhiteSpace(name.Text)) throw new DeploymentException("NameRequired");
        var exePath = PathPolicy.Full(exe.Text);
        if (!File.Exists(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new DeploymentException("GameUnavailable", exePath);
        var root = PathPolicy.Full(target.Text); var installRoot = PathPolicy.Full(install.Text);
        if (!Directory.Exists(root) || !Directory.Exists(installRoot) || !PathPolicy.Within(exePath, installRoot))
            throw new DeploymentException("GameUnavailable", root);
        var architecture = PeInspector.Architecture(exePath);
        if (architecture == "Unknown") throw new DeploymentException("InvalidExecutable", exePath);
        var state = c.Session.State!;
        if (existing is not null && state.Receipts.Any(r => r.GameId == existing.Id) &&
            (!PathPolicy.Comparer.Equals(existing.ExePath, exePath) || !PathPolicy.Comparer.Equals(existing.DeploymentRoot, root) ||
             !PathPolicy.Comparer.Equals(existing.InstallRoot, installRoot))) throw new DeploymentException("ManagedLocationLocked");
        PathPolicy.Disjoint(root, c.Session.Store!.Root); PathPolicy.Disjoint(installRoot, c.Session.Store.Root);
        foreach (var other in state.Games.Where(g => g.Id != existing?.Id)) PathPolicy.Disjoint(root, other.DeploymentRoot);
        foreach (var source in state.Libraries.SelectMany(l => l.Versions).Select(v => v.SourcePath))
        { PathPolicy.Disjoint(source, root); PathPolicy.Disjoint(source, installRoot); }
        var game = new GameEntry(existing?.Id ?? Guid.NewGuid(), name.Text.Trim(), installRoot, exePath, root,
            apiNames[Math.Max(0, api.SelectedIndex)], architecture, existing?.Source ?? discovered?.Source ?? "Manual");
        c.GameId = game.Id;
        c.Session.Save(state with { Games = state.Games.Where(g => g.Id != game.Id).Append(game).ToArray() });
    }

    private async Task ChooseVersion(GameEntry game, ResourceLibrary library, DeploymentReceipt receipt)
    {
        var versions = library.Versions.Where(v => v.Id != receipt.VersionId && SourceAvailable(v)).ToArray();
        var box = Ui.Choice("CandidateVersion", versions.Select(ResourcePresentation.VersionName));
        if (await c.Confirm("ChangeVersion", Ui.Stack(Ui.Text(Strings.Get("CurrentVersion") + ": " + receipt.VersionLabel), box), "Preflight", versions.Length > 0))
            await Preflight(game, [new(library.Id, versions[box.SelectedIndex].Id, OperationKind.Change)]);
    }
}
