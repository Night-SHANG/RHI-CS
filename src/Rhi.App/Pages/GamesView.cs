using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.App.Pages;

public sealed partial class GamesView : UserControl
{
    private readonly PageContext c;
    private readonly Dictionary<Guid, Guid> selected = [];
    private Button? batchButton;
    private Button? clearBatchButton;
    private CheckBox? selectAll;
    private readonly List<CheckBox> selectionBoxes = [];
    private bool updatingAll;
    public GamesView(PageContext context)
    {
        c = context;
        pendingStamp = AtomicJson.Fingerprint(c.Session.Store!.Pending());
        var state = c.Session.State!;
        var list = new ListView { DisplayMemberPath = "Name" };
        var detail = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        var search = new TextBox { PlaceholderText = Strings.Get("SearchGames"), Text = c.GameQuery };
        AutomationProperties.SetAutomationId(search, "GamesSearch");
        var empty = Ui.Label(state.Games.Length == 0 ? "NoGames" : "NoMatches");
        var filtering = false;
        void ShowGame(GameEntry? game)
        {
            var stamp = AtomicJson.Fingerprint(new { Game = game, c.Session.State!.Libraries,
                LocationAvailable = game is not null && Directory.Exists(game.InstallRoot) && Directory.Exists(game.DeploymentRoot) && File.Exists(game.ExePath) });
            if (detail.Content is not null && c.GameId == game?.Id && gameStamp == stamp) return;
            gameStamp = stamp;
            c.GameId = game?.Id;
            selected.Clear(); selectionBoxes.Clear(); batchButton = null; clearBatchButton = null; selectAll = null;
            rowRefreshers.Clear(); versionChoices.Clear();
            detail.Content = GameBody(game);
        }
        void Filter()
        {
            var state = c.Session.State!;
            c.GameQuery = search.Text;
            var filtered = state.Games.Where(g => g.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            var game = filtered.FirstOrDefault(g => g.Id == c.GameId) ?? filtered.FirstOrDefault();
            filtering = true;
            if (list.ItemsSource is not GameEntry[] shown || !shown.SequenceEqual(filtered)) list.ItemsSource = filtered;
            if (!Equals(list.SelectedItem, game)) list.SelectedItem = game;
            filtering = false;
            empty.Text = Strings.Get(state.Games.Length == 0 ? "NoGames" : "NoMatches");
            empty.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowGame(game);
        }
        refreshData = () => { Filter(); foreach (var refresh in rowRefreshers.ToArray()) refresh(); UpdateSelection(); };
        list.SelectionChanged += (_, _) => { if (!filtering) ShowGame(list.SelectedItem as GameEntry); };
        search.TextChanged += (_, _) => Filter();
        Filter();
        var left = Ui.Stack(Ui.Label("Games", 23), search,
            Ui.Row(c.Button("Refresh", Scan, !c.Session.Busy), c.Button("AddGame", () => EditGame(), !c.Session.Busy)), empty, list);
        Content = Ui.Split(Ui.Scroll(left), detail);
    }

    private UIElement GameBody(GameEntry? game)
    {
        var state = c.Session.State!;
        var body = Ui.Stack();
        if (game is null) body.Children.Add(Ui.Label(state.Games.Length == 0 ? "AddGameHelp" : "NoMatches", 22));
        else
        {
            c.GameId = game.Id;
            body.Children.Add(Ui.Text(game.Name, 28));
            body.Children.Add(Ui.Secondary($"{game.Api} · {game.Architecture}"));
            body.Children.Add(Ui.Secondary(Strings.Get("DeploymentRoot") + ": " + game.DeploymentRoot));
            var pending = c.Session.Store!.Pending().Where(j => j.GameId == game.Id).ToArray();
            var locationAvailable = Directory.Exists(game.InstallRoot) && Directory.Exists(game.DeploymentRoot) && File.Exists(game.ExePath);
            var edit = c.Button("Edit", () => EditGame(game), !c.Session.Busy && pending.Length == 0);
            AutomationProperties.SetAutomationId(edit, "GameEdit");
            var inspect = c.Button("CheckTargets", async () => { await c.Session.InspectTargets(game.Id); },
                !c.Session.Busy && state.Receipts.Any(r => r.GameId == game.Id));
            AutomationProperties.SetAutomationId(inspect, "GameInspectTargets");
            var headerButtons = new List<Button> { edit, inspect };
            if (state.Receipts.Any(r => r.GameId == game.Id) || pending.Length > 0)
            {
                var relocate = c.Button("RelocateGame", () => Relocate(game), !c.Session.Busy && pending.Length == 0);
                AutomationProperties.SetAutomationId(relocate, "GameRelocate");
                headerButtons.Add(relocate);
            }
            headerButtons.Add(c.FolderAction("OpenDeploymentFolder", game.DeploymentRoot, "GameOpenFolder"));
            var header = Ui.HeaderActions("GameHeaderActions", headerButtons.ToArray());
            body.Children.Add(header);
            var hasRelocate = headerButtons.Any(b => AutomationProperties.GetAutomationId(b) == "GameRelocate");
            rowRefreshers.Add(() =>
            {
                inspect.IsEnabled = c.Session.State!.Receipts.Any(r => r.GameId == game.Id);
                var needed = inspect.IsEnabled || c.Session.Store!.Pending().Any(j => j.GameId == game.Id);
                if (needed == hasRelocate) return;
                hasRelocate = needed;
                if (needed)
                {
                    var button = c.Button("RelocateGame", () => Relocate(game), pending.Length == 0);
                    AutomationProperties.SetAutomationId(button, "GameRelocate");
                    headerButtons.Insert(2, button);
                }
                else headerButtons.RemoveAll(b => AutomationProperties.GetAutomationId(b) == "GameRelocate");
                var index = body.Children.IndexOf(header);
                foreach (var button in headerButtons) header.Children.Remove(button);
                body.Children[index] = header = Ui.HeaderActions("GameHeaderActions", headerButtons.ToArray());
            });
            if (pending.Length > 0) body.Children.Add(Ui.Label("PendingConfigurationLocked"));
            if (!locationAvailable) body.Children.Add(Ui.Label("GameLocationMissing"));
            var lastChecked = Ui.Text("");
            void UpdateChecked()
            {
                var observed = c.Session.TargetObservation;
                lastChecked.Text = observed?.GameId == game.Id && observed.Revision == c.Session.State!.Revision
                    ? Strings.Format("LastChecked", observed.CheckedAt.ToLocalTime()) : "";
                lastChecked.Visibility = lastChecked.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateChecked(); rowRefreshers.Add(UpdateChecked); body.Children.Add(lastChecked);
            foreach (var operation in pending)
                body.Children.Add(Ui.Stack(Ui.Label("NeedsRepair"), Ui.Text(operation.Error ?? ""),
                    c.Button("CheckRecovery", () => Recover(operation), !c.Session.Busy)));
            body.Children.Add(Ui.Label("LocalResources", 20));
            body.Children.Add(Ui.Label("PreflightHelp"));
            var table = new StackPanel();
            selectAll = new CheckBox { MinWidth = 0, Width = 32 };
            AutomationProperties.SetAutomationId(selectAll, "SelectAllLibraries");
            AutomationProperties.SetName(selectAll, Strings.Get("SelectAll"));
            ToolTipService.SetToolTip(selectAll, Strings.Get("SelectAll"));
            void ToggleAll(object sender, RoutedEventArgs args)
            {
                if (updatingAll) return;
                var value = selectionBoxes.Any(b => b.IsEnabled && b.IsChecked != true);
                foreach (var check in selectionBoxes.Where(b => b.IsEnabled)) check.IsChecked = value;
                UpdateSelection();
            }
            selectAll.Checked += ToggleAll;
            selectAll.Unchecked += ToggleAll;
            selectAll.Indeterminate += ToggleAll;
            table.Children.Add(Ui.TableLine(TableRow(true, selectAll, Ui.Label("LibraryName"), Ui.Label("VersionLabel"), Ui.Label("Status"), Ui.Label("Actions")), true));
            foreach (var library in state.Libraries) table.Children.Add(RetainedResourceRow(game, library, pending.Length > 0 || !locationAvailable));
            body.Children.Add(table);
            if (state.Libraries.Length == 0) body.Children.Add(Ui.Label("NoLibraries"));
        }
        batchButton = c.Button("PreflightSelected", async () =>
        {
            if (game is not null) await Preflight(game, SelectedRequests(clear: false));
        }, false);
        AutomationProperties.SetAutomationId(batchButton, "BatchPreflight");
        clearBatchButton = c.Button("ClearDeployment", async () =>
        {
            if (game is not null) await Preflight(game, SelectedRequests(clear: true));
        }, false);
        AutomationProperties.SetAutomationId(clearBatchButton, "BatchClear");
        UpdateSelection();
        var scroll = Ui.Scroll(body);
        AutomationProperties.SetAutomationId(scroll, "GamesScroll");
        return Ui.Dock(scroll, Ui.Stack(Ui.Label("BatchSelectionHelp"), Ui.Row(batchButton, clearBatchButton)));
    }

    private UIElement ResourceRow(GameEntry game, ResourceLibrary library, bool locked)
    {
        var receipt = c.Session.State!.Receipts.SingleOrDefault(r => r.GameId == game.Id && r.LibraryId == library.Id);
        var versions = library.Versions;
        var box = new ComboBox { ItemsSource = versions.Select(ResourcePresentation.VersionName).ToArray(),
            MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        box.SelectedIndex = receipt is null ? Array.FindIndex(versions, SourceAvailable) : Array.FindIndex(versions, v => v.Id == receipt.VersionId);
        if (versionChoices.TryGetValue(library.Id, out var choice) && Array.FindIndex(versions, v => v.Id == choice) is var chosen && chosen >= 0)
            box.SelectedIndex = chosen;
        AutomationProperties.SetAutomationId(box, "VersionChoice_" + library.Id.ToString("N"));
        var check = new CheckBox { MinWidth = 0, Width = 32, IsChecked = selected.ContainsKey(library.Id) };
        selectionBoxes.Add(check);
        AutomationProperties.SetAutomationId(check, "SelectLibrary_" + library.Id.ToString("N"));
        AutomationProperties.SetName(check, Strings.Get("Select") + " " + library.Name);
        var deploy = c.Button(receipt is null ? "Deploy" : "ChangeVersion", async () =>
        {
            if (box.SelectedIndex >= 0) await Preflight(game, [new(library.Id, versions[box.SelectedIndex].Id,
                receipt is null ? OperationKind.Deploy : OperationKind.Change)]);
        });
        AutomationProperties.SetAutomationId(deploy, (receipt is null ? "DeployLibrary_" : "ChangeLibrary_") + library.Id.ToString("N"));
        var status = Ui.Stack(Ui.Status(receipt is null ? "NotDeployed" : "Installed", true));
        status.Spacing = 4;
        var versionCell = Ui.Stack(Ui.OneLine(receipt is null ? Strings.Get("PendingVersion") :
            Strings.Get("CurrentVersion") + ": " + receipt.VersionLabel, true), box);
        versionCell.Spacing = 4;
        var actionButtons = new List<Button> { deploy };
        if (receipt is not null)
        {
            var clear = c.Button("ClearDeployment", () => Preflight(game, [new(library.Id, null, OperationKind.Clear)]), !c.Session.Busy && !locked);
            AutomationProperties.SetAutomationId(clear, "ClearLibrary_" + library.Id.ToString("N"));
            actionButtons.Add(clear);
            actionButtons.Add(c.Button("ViewDifferences", () => ViewDifferences(game, library, receipt), !c.Session.Busy));
            var snapshot = c.Session.TargetObservation;
            var health = snapshot?.GameId == game.Id && snapshot.Revision == c.Session.State.Revision
                ? snapshot.Receipts.FirstOrDefault(r => r.LibraryId == library.Id)?.Health.ToString() : null;
            status.Children.Add(Ui.Status(health ?? "NotChecked", true));
        }
        var actions = Ui.CompactActions(actionButtons.ToArray());
        void Update()
        {
            if (box.SelectedIndex >= 0) versionChoices[library.Id] = versions[box.SelectedIndex].Id;
            ToolTipService.SetToolTip(box, box.SelectedIndex >= 0 ? ResourcePresentation.VersionName(versions[box.SelectedIndex]) : null);
            var sourceAvailable = box.SelectedIndex >= 0 && SourceAvailable(versions[box.SelectedIndex]);
            var usable = !c.Session.Busy && !locked && sourceAvailable;
            var same = receipt is not null && box.SelectedIndex >= 0 && versions[box.SelectedIndex].Id == receipt.VersionId;
            check.IsEnabled = !c.Session.Busy && !locked && (receipt is not null || usable);
            deploy.IsEnabled = usable && !same; box.IsEnabled = !c.Session.Busy && !locked;
            if (!check.IsEnabled) check.IsChecked = false;
            var reason = !sourceAvailable ? "SourceUnavailable" : same
                ? versions.Any(v => v.Id != receipt!.VersionId && SourceAvailable(v)) ? "ChooseOtherVersion" : "NoOtherVersion" : null;
            var help = reason is null ? "" : Strings.Get(reason);
            var fullVersion = box.SelectedIndex >= 0 ? ResourcePresentation.VersionName(versions[box.SelectedIndex]) : "";
            ToolTipService.SetToolTip(box, help.Length == 0 ? fullVersion : fullVersion + "\n" + help);
            AutomationProperties.SetHelpText(box, help);
            ToolTipService.SetToolTip(actions, help);
            AutomationProperties.SetHelpText(deploy, help);
            if (receipt is null)
            {
                status.Children.Clear();
                status.Children.Add(Ui.Status(sourceAvailable ? "NotDeployed" : "SourceUnavailable", true));
            }
            if (check.IsChecked == true) selected[library.Id] = box.SelectedIndex >= 0 ? versions[box.SelectedIndex].Id : Guid.Empty;
            else selected.Remove(library.Id);
            UpdateSelection();
        }
        check.Checked += (_, _) => Update(); check.Unchecked += (_, _) => Update();
        box.SelectionChanged += (_, _) => Update(); Update();
        var row = TableRow(false, check, Ui.OneLine(library.Name), versionCell, status, actions);
        AutomationProperties.SetAutomationId(row, "ResourceRow_" + library.Id.ToString("N"));
        return row;
    }

    private void UpdateSelection()
    {
        if (batchButton is null) return;
        var requests = SelectedRequests(clear: false);
        var deployCount = requests.Length;
        var clearCount = SelectedRequests(clear: true).Length;
        var batchKey = requests.Any(r => r.Kind == OperationKind.Deploy)
            ? requests.Any(r => r.Kind == OperationKind.Change) ? "BatchDeployCount" : "BatchInstallCount"
            : "BatchChangeCount";
        Ui.ButtonContent(batchButton, Strings.Format(batchKey, deployCount));
        batchButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        batchButton.IsEnabled = deployCount > 0 && !c.Session.Busy;
        if (clearBatchButton is not null)
        {
            Ui.ButtonContent(clearBatchButton, Strings.Format("BatchClearCount", clearCount));
            clearBatchButton.IsEnabled = clearCount > 0 && !c.Session.Busy;
        }
        if (selectAll is not null)
        {
            updatingAll = true;
            var eligible = selectionBoxes.Where(b => b.IsEnabled).ToArray();
            var count = eligible.Count(b => b.IsChecked == true);
            selectAll.IsEnabled = eligible.Length > 0 && !c.Session.Busy;
            selectAll.IsChecked = count == 0 ? false : count == eligible.Length ? true : null;
            updatingAll = false;
        }
    }

    private DeploymentRequest[] SelectedRequests(bool clear)
    {
        var state = c.Session.State!;
        return selected.Select(pair =>
        {
            var receipt = state.Receipts.FirstOrDefault(r => r.GameId == c.GameId && r.LibraryId == pair.Key);
            if (clear) return receipt is null ? null : new DeploymentRequest(pair.Key, null, OperationKind.Clear);
            var version = state.Libraries.Single(l => l.Id == pair.Key).Versions.FirstOrDefault(v => v.Id == pair.Value);
            return version is null || !SourceAvailable(version) || receipt?.VersionId == version.Id ? null :
                new DeploymentRequest(pair.Key, version.Id, receipt is null ? OperationKind.Deploy : OperationKind.Change);
        }).OfType<DeploymentRequest>().ToArray();
    }
    private static bool SourceAvailable(ResourceVersion v) => ResourcePresentation.SourceAvailable(v);
    private static Grid TableRow(bool header, params FrameworkElement[] cells)
    {
        return Ui.ResourceTableRow(header, true, cells);
    }
}
