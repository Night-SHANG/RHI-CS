using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    public GamesView(PageContext context)
    {
        c = context;
        var state = c.Session.State!;
        var list = new ListView { DisplayMemberPath = "Name", ItemsSource = state.Games };
        var game = state.Games.FirstOrDefault(g => g.Id == c.GameId) ?? state.Games.FirstOrDefault();
        list.SelectedItem = game;
        list.SelectionChanged += (_, _) => { c.GameId = (list.SelectedItem as GameEntry)?.Id; c.Refresh(); };
        var search = new TextBox { PlaceholderText = Strings.Get("SearchGames") };
        var empty = Ui.Label(state.Games.Length == 0 ? "NoGames" : "NoMatches");
        empty.Visibility = state.Games.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        search.TextChanged += (_, _) =>
        {
            var filtered = state.Games.Where(g => g.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            list.ItemsSource = filtered;
            empty.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        var left = Ui.Stack(Ui.Label("Games", 23), search,
            Ui.Row(c.Button("Refresh", Scan, !c.Session.Busy), c.Button("AddGame", () => EditGame(), !c.Session.Busy)), empty, list);
        var body = Ui.Stack();
        if (game is null) body.Children.Add(Ui.Label("AddGameHelp", 22));
        else
        {
            c.GameId = game.Id;
            body.Children.Add(Ui.Text(game.Name, 28));
            body.Children.Add(Ui.Text($"{game.Api} · {game.Architecture}"));
            body.Children.Add(Ui.Text(Strings.Get("DeploymentRoot") + ": " + game.DeploymentRoot));
            var pending = c.Session.Store!.Pending().Where(j => j.GameId == game.Id).ToArray();
            body.Children.Add(c.Button("Edit", () => EditGame(game), !c.Session.Busy && pending.Length == 0));
            foreach (var operation in pending)
                body.Children.Add(Ui.Stack(Ui.Label("NeedsRepair"), Ui.Text(operation.Error ?? ""),
                    c.Button("CheckRecovery", () => Recover(operation), !c.Session.Busy)));
            body.Children.Add(Ui.Label("LocalResources", 20));
            body.Children.Add(Ui.Label("PreflightHelp"));
            body.Children.Add(TableRow(Ui.Label("Select"), Ui.Label("LibraryName"), Ui.Label("VersionLabel"), Ui.Label("Status"), Ui.Label("Actions")));
            foreach (var library in state.Libraries) body.Children.Add(ResourceRow(game, library, pending.Length > 0));
            if (state.Libraries.Length == 0) body.Children.Add(Ui.Label("NoLibraries"));
        }
        batchButton = c.Button("PreflightSelected", async () =>
        {
            if (game is not null) await Preflight(game, selected.Select(s => new DeploymentRequest(s.Key, s.Value, OperationKind.Deploy)).ToArray());
        }, false);
        UpdateSelection();
        Content = Ui.Split(Ui.Scroll(left), Ui.Dock(Ui.Scroll(body), batchButton));
    }

    private UIElement ResourceRow(GameEntry game, ResourceLibrary library, bool locked)
    {
        var receipt = c.Session.State!.Receipts.SingleOrDefault(r => r.GameId == game.Id && r.LibraryId == library.Id);
        if (receipt is not null)
        {
            var actions = Ui.Row(c.Button("ChangeVersion", () => ChooseVersion(game, library, receipt),
                !c.Session.Busy && !locked && library.Versions.Any(v => v.Id != receipt.VersionId && SourceAvailable(v))),
                c.Button("ClearDeployment", () => Preflight(game, [new(library.Id, null, OperationKind.Clear)]), !c.Session.Busy && !locked));
            var status = Ui.Stack(Ui.Label("Installed"), Ui.Label("NotChecked"));
            if (!library.Versions.Any(v => v.Id != receipt.VersionId && SourceAvailable(v))) status.Children.Add(Ui.Label("NoOtherVersion"));
            if (!library.Versions.Any(v => v.Id == receipt.VersionId && SourceAvailable(v))) status.Children.Add(Ui.Label("SourceUnavailable"));
            return TableRow(Ui.Text(""), Ui.Text(library.Name), Ui.Text(receipt.VersionLabel), status, actions);
        }
        var versions = library.Versions;
        var box = new ComboBox { ItemsSource = versions.Select(v => v.Label).ToArray(), MinWidth = 150, MaxWidth = 230 };
        box.SelectedIndex = Array.FindIndex(versions, SourceAvailable);
        var check = new CheckBox();
        var deploy = c.Button("Deploy", async () =>
        {
            if (box.SelectedIndex >= 0) await Preflight(game, [new(library.Id, versions[box.SelectedIndex].Id, OperationKind.Deploy)]);
        });
        var statusText = Ui.Label("NotDeployed");
        void Update()
        {
            var usable = !c.Session.Busy && !locked && box.SelectedIndex >= 0 && SourceAvailable(versions[box.SelectedIndex]);
            check.IsEnabled = usable; deploy.IsEnabled = usable; box.IsEnabled = !c.Session.Busy && !locked;
            if (!usable) check.IsChecked = false;
            statusText.Text = Strings.Get(box.SelectedIndex >= 0 && SourceAvailable(versions[box.SelectedIndex]) ? "NotDeployed" : "SourceUnavailable");
            if (check.IsChecked == true) selected[library.Id] = versions[box.SelectedIndex].Id;
            else selected.Remove(library.Id);
            UpdateSelection();
        }
        check.Checked += (_, _) => Update(); check.Unchecked += (_, _) => Update();
        box.SelectionChanged += (_, _) => Update(); Update();
        return TableRow(check, Ui.Text(library.Name), box, statusText, deploy);
    }

    private void UpdateSelection()
    {
        if (batchButton is null) return;
        batchButton.Content = Strings.Format("PreflightCount", selected.Count);
        batchButton.IsEnabled = selected.Count > 0 && !c.Session.Busy;
        batchButton.HorizontalAlignment = HorizontalAlignment.Right;
    }
    private static bool SourceAvailable(ResourceVersion v) => v.Kind == ResourceKind.Files && (File.Exists(v.SourcePath) || Directory.Exists(v.SourcePath));
    private static Grid TableRow(params FrameworkElement[] cells)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12), MinHeight = 62 };
        foreach (var width in new[] { 36d, 1.1, 1.0, 1.0, 2.0 })
            grid.ColumnDefinitions.Add(new() { Width = width == 36 ? new GridLength(width) : new GridLength(width, GridUnitType.Star) });
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        return grid;
    }
}
