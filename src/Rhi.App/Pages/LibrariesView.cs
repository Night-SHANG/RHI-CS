using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.App.Pages;

public sealed class LibrariesView : UserControl
{
    private readonly PageContext c;
    public LibrariesView(PageContext context)
    {
        c = context;
        var state = c.Session.State!;
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, DisplayMemberPath = "Name" };
        var detail = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        var search = new TextBox { PlaceholderText = Strings.Get("SearchLibraries"), Text = c.LibraryQuery };
        var empty = Ui.Label(state.Libraries.Length == 0 ? "NoLibraries" : "NoMatches");
        var filtering = false;
        void ShowLibrary(ResourceLibrary? library)
        {
            if (detail.Content is not null && c.LibraryId == library?.Id) return;
            c.LibraryId = library?.Id;
            detail.Content = LibraryBody(library);
        }
        void Filter()
        {
            c.LibraryQuery = search.Text;
            var filtered = state.Libraries.Where(l => l.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            var library = filtered.FirstOrDefault(l => l.Id == c.LibraryId) ?? filtered.FirstOrDefault();
            filtering = true;
            list.ItemsSource = filtered; list.SelectedItem = library;
            filtering = false;
            empty.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowLibrary(library);
        }
        list.SelectionChanged += (_, _) => { if (!filtering) ShowLibrary(list.SelectedItem as ResourceLibrary); };
        search.TextChanged += (_, _) => Filter();
        Filter();
        var left = Ui.Stack(Ui.Label("Libraries", 23), search, c.Button("AddLibrary", () => EditLibrary(null), !c.Session.Busy), list);
        left.Children.Add(empty);
        Content = Ui.Split(Ui.Scroll(left), detail);
    }

    private UIElement LibraryBody(ResourceLibrary? selected)
    {
        var state = c.Session.State!;
        var right = Ui.Stack(Ui.Label("Libraries", 28), Ui.Label("LibraryExplanation"));
        if (selected is null) right.Children.Add(Ui.Label("NoLibraries"));
        else
        {
            right.Children.Add(Ui.Text(selected.Name, 23));
            right.Children.Add(Ui.Text(Strings.Get("TargetRelative") + ": " + selected.TargetRelative));
            right.Children.Add(Ui.Row(c.Button("Edit", () => EditLibrary(selected), !c.Session.Busy),
                c.Button("AddVersion", () => EditVersion(selected), !c.Session.Busy)));
            right.Children.Add(Ui.Label("Versions", 20));
            right.Children.Add(Ui.Columns([1, 2, 1, 1], Ui.Label("VersionLabel"), Ui.Label("SourcePath"), Ui.Label("Status"), Ui.Label("Actions")));
            foreach (var version in selected.Versions)
            {
                var references = state.Receipts.Where(r => r.LibraryId == selected.Id && r.VersionId == version.Id)
                    .Select(r => state.Games.First(g => g.Id == r.GameId).Name);
                var available = File.Exists(version.SourcePath) || Directory.Exists(version.SourcePath);
                right.Children.Add(Ui.Columns([1, 2, 1, 1], Ui.Text(version.Label), Ui.Text(version.SourcePath),
                    Ui.Stack(Ui.Label(available ? "Available" : "SourceUnavailable"), Ui.Text(string.Join(", ", references))),
                    c.Action("View", () => { c.VersionId = version.Id; c.Refresh(); })));
            }
            var detail = selected.Versions.FirstOrDefault(v => v.Id == c.VersionId) ?? selected.Versions.FirstOrDefault();
            if (detail is not null) right.Children.Add(Ui.Card(Ui.Stack(Ui.Label("VersionDetails", 20), Ui.Text(detail.Label),
                Ui.Text(detail.SourcePath), Ui.Text(Strings.Get("TargetRelative") + ": " + (detail.TargetOverride ?? selected.TargetRelative)),
                c.Button("Edit", () => EditVersion(selected, detail), !c.Session.Busy))));
        }
        return Ui.Dock(Ui.Scroll(right), Ui.Label("ConfigOnly"));
    }

    private async Task EditLibrary(ResourceLibrary? existing)
    {
        var name = Ui.Input("LibraryName", existing?.Name ?? "");
        var target = Ui.Input("TargetRelative", existing?.TargetRelative ?? "");
        if (!await c.Confirm(existing is null ? "AddLibrary" : "Edit", Ui.Stack(name, target, Ui.Label("ConfigOnly")))) return;
        if (string.IsNullOrWhiteSpace(name.Text)) throw new DeploymentException("NameRequired");
        var library = new ResourceLibrary(existing?.Id ?? Guid.NewGuid(), name.Text.Trim(), PathPolicy.Relative(target.Text), existing?.Versions ?? []);
        var state = c.Session.State!;
        c.LibraryId = library.Id;
        c.Session.Save(state with { Libraries = state.Libraries.Where(l => l.Id != library.Id).Append(library).ToArray() });
    }

    private async Task EditVersion(ResourceLibrary library, ResourceVersion? existing = null)
    {
        var label = Ui.Input("VersionLabel", existing?.Label ?? "");
        var source = Ui.Input("SourcePath", existing?.SourcePath ?? "");
        var target = Ui.Input("TargetOverride", existing?.TargetOverride ?? "");
        var chooseFolder = c.Button("ChooseFolder", async () => { var p = await c.Windows.PickFolder(); if (p is not null) source.Text = p; });
        var chooseFile = c.Button("ChooseFile", async () => { var p = await c.Windows.PickFile(); if (p is not null) source.Text = p; });
        if (!await c.Confirm(existing is null ? "AddVersion" : "Edit", Ui.Stack(label, source, Ui.Row(chooseFolder, chooseFile), target,
            Ui.Label("ConfigOnly"), Ui.Label("ReShadePending")))) return;
        if (string.IsNullOrWhiteSpace(label.Text)) throw new DeploymentException("NameRequired");
        var path = PathPolicy.Full(source.Text);
        if (!Directory.Exists(path) && !File.Exists(path)) throw new DeploymentException("SourceUnavailable", path);
        PathPolicy.Disjoint(path, c.Session.Store!.Root);
        foreach (var game in c.Session.State!.Games)
        { PathPolicy.Disjoint(path, game.InstallRoot); PathPolicy.Disjoint(path, game.DeploymentRoot); }
        var version = new ResourceVersion(existing?.Id ?? Guid.NewGuid(), label.Text.Trim(), path,
            string.IsNullOrWhiteSpace(target.Text) ? null : PathPolicy.Relative(target.Text), ResourceKind.Files);
        var updated = library with { Versions = library.Versions.Where(v => v.Id != version.Id).Append(version).ToArray() };
        c.Session.Save(c.Session.State with { Libraries = c.Session.State.Libraries.Select(l => l.Id == library.Id ? updated : l).ToArray() });
    }
}
