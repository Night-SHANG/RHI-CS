using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Configuration;

namespace Rhi.App.Pages;

public sealed partial class LibrariesView : UserControl
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
        AutomationProperties.SetAutomationId(search, "LibrariesSearch");
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
        var add = c.Button("AddLibrary", () => EditLibrary(null), !c.Session.Busy);
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        add.MinHeight = 44;
        AutomationProperties.SetAutomationId(add, "AddLibrary");
        var left = Ui.Stack(Ui.Label("Libraries", 23), search, add, list);
        left.Children.Add(empty);
        Content = Ui.Split(Ui.Scroll(left), detail);
    }

    private UIElement LibraryBody(ResourceLibrary? selected)
    {
        var state = c.Session.State!;
        var right = Ui.Stack(Ui.Label("Libraries", 28), Ui.Label("LibraryExplanation"));
        var locked = selected is not null && c.Session.Store!.Pending().Any(j => j.LibraryId == selected.Id);
        if (selected is null) right.Children.Add(Ui.Label(state.Libraries.Length == 0 ? "NoLibraries" : "NoMatches"));
        else
        {
            right.Children.Add(Ui.Text(selected.Name, 23));
            right.Children.Add(Ui.Text(ResourcePresentation.TargetLocation(selected.TargetRelative)));
            var editLibrary = c.Button("Edit", () => EditLibrary(selected), !c.Session.Busy && !locked);
            var addVersion = c.Button("AddVersion", () => EditVersion(selected), !c.Session.Busy && !locked);
            AutomationProperties.SetAutomationId(editLibrary, "LibraryEdit");
            AutomationProperties.SetAutomationId(addVersion, "LibraryAddVersion");
            var removeLibrary = c.Button("RemoveLibrary", () => RemoveLibrary(selected),
                !c.Session.Busy && !locked && !state.Receipts.Any(r => r.LibraryId == selected.Id));
            AutomationProperties.SetAutomationId(removeLibrary, "LibraryRemove");
            right.Children.Add(Ui.Row(editLibrary, addVersion, removeLibrary));
            AddReferenceNotice(right, selected.Id);
            right.Children.Add(Ui.Label("Versions", 20));
            var table = new StackPanel();
            table.Children.Add(Ui.TableLine(Ui.Columns([1, 2, 1, 1], Ui.Label("VersionLabel"), Ui.Label("SourcePath"), Ui.Label("Status"), Ui.Label("Actions")), true));
            foreach (var version in selected.Versions)
            {
                var references = state.Receipts.Where(r => r.LibraryId == selected.Id && r.VersionId == version.Id)
                    .Select(r => state.Games.First(g => g.Id == r.GameId).Name);
                var available = ResourcePresentation.SourceAvailable(version);
                table.Children.Add(Ui.TableLine(Ui.Columns([1, 2, 1, 1], Ui.Text(ResourcePresentation.VersionName(version)), Ui.Text(version.SourcePath),
                    Ui.Stack(Ui.Status(available ? "Available" : "SourceUnavailable"), Ui.Secondary(string.Join(", ", references))),
                    c.Action("View", () => { c.VersionId = version.Id; c.Refresh(); }))));
            }
            right.Children.Add(table);
            var detail = selected.Versions.FirstOrDefault(v => v.Id == c.VersionId) ?? selected.Versions.FirstOrDefault();
            if (detail is not null)
            {
                var editVersion = c.Button("Edit", () => EditVersion(selected, detail), !c.Session.Busy && !locked);
                AutomationProperties.SetAutomationId(editVersion, "VersionEdit");
                var removeVersion = c.Button("RemoveVersion", () => RemoveVersion(selected, detail),
                    !c.Session.Busy && !locked && !state.Receipts.Any(r => r.LibraryId == selected.Id && r.VersionId == detail.Id));
                AutomationProperties.SetAutomationId(removeVersion, "VersionRemove");
                right.Children.Add(Ui.Card(Ui.Stack(Ui.Label("VersionDetails", 20), Ui.Text(ResourcePresentation.VersionName(detail)),
                    Ui.Text(Strings.Get("SourcePath") + ": " + detail.SourcePath),
                    Ui.Text(ResourcePresentation.TargetLocation(detail.Kind == ResourceKind.Files
                        ? detail.TargetOverride ?? selected.TargetRelative : "")),
                    Ui.Row(editVersion, removeVersion))));
            }
        }
        var scroll = Ui.Scroll(right);
        AutomationProperties.SetAutomationId(scroll, "LibraryScroll");
        return Ui.Dock(scroll, Ui.Label(locked ? "PendingConfigurationLocked" : "ConfigOnly"));
    }

    private async Task EditLibrary(ResourceLibrary? existing)
    {
        var name = Ui.Input("LibraryName", existing?.Name ?? "");
        var target = Ui.Input("TargetRelative", existing?.TargetRelative ?? "");
        target.PlaceholderText = Strings.Get("LibraryTargetPlaceholder");
        AutomationProperties.SetAutomationId(name, "LibraryNameInput");
        AutomationProperties.SetAutomationId(target, "LibraryTargetInput");
        var id = existing?.Id ?? Guid.NewGuid();
        ResourceLibrary Read() => ConfigurationRules.Library(new(id, name.Text, target.Text, existing?.Versions ?? []));
        await c.Edit(existing is null ? "AddLibrary" : "Edit",
            Ui.Stack(Ui.Label("LibraryFormHelp"), name, Ui.Stack(target, Ui.Label("LibraryTargetHelp")), Ui.Label("ConfigOnly")),
            () => { Read(); }, () =>
            {
                var library = Read(); var state = c.Session.State!;
                c.Session.Save(state with { Libraries = state.Libraries.Where(l => l.Id != library.Id).Append(library).ToArray() });
                c.LibraryId = library.Id;
                c.Refresh();
                return Task.CompletedTask;
            }, [name, target]);
    }

    private async Task EditVersion(ResourceLibrary library, ResourceVersion? existing = null)
    {
        var label = Ui.Input("VersionLabel", existing?.Label ?? "");
        var source = Ui.Input("SourcePath", existing?.SourcePath ?? "");
        var target = Ui.Input("TargetOverride", existing?.TargetOverride ?? "");
        // New offline resources are ordinary files. Preserve legacy adapter semantics when editing old records.
        var kind = existing?.Kind ?? ResourceKind.Files;
        var ordinary = kind == ResourceKind.Files;
        source.PlaceholderText = Strings.Get("SourcePlaceholder");
        target.PlaceholderText = Strings.Get("VersionTargetPlaceholder");
        AutomationProperties.SetAutomationId(label, "VersionLabelInput");
        AutomationProperties.SetAutomationId(source, "VersionSourceInput");
        AutomationProperties.SetAutomationId(target, "VersionTargetOverride");
        var chooseFolder = c.Button("ChooseFolder", async () => { var p = await c.Windows.PickFolder(); if (p is not null) source.Text = p; });
        AutomationProperties.SetAutomationId(chooseFolder, "VersionChooseFolder");
        var chooseFile = c.Button("ChooseFile", async () => { var p = await c.Windows.PickFile(); if (p is not null) source.Text = p; });
        AutomationProperties.SetAutomationId(chooseFile, "VersionChooseFile");
        chooseFolder.IsEnabled = ordinary;
        target.IsEnabled = ordinary;
        if (!ordinary) target.Text = "";
        var sourceGroup = Ui.Stack(source, Ui.Row(chooseFolder, chooseFile), Ui.Label(ordinary ? "VersionSourceHelp" : "ReShadePending"));
        var targetGroup = Ui.Stack(target, ordinary
            ? Ui.Text(Strings.Format("VersionTargetHelp", ResourcePresentation.TargetLocation(library.TargetRelative)))
            : Ui.Label("ReShadeTargetMustBeRoot"));
        sourceGroup.Spacing = 6;
        targetGroup.Spacing = 6;
        var form = Ui.Stack(label, sourceGroup, targetGroup);
        var id = existing?.Id ?? Guid.NewGuid();
        ResourceVersion Read() => ConfigurationRules.Version(c.Session.State!, c.Session.Store!.Root,
            new(id, label.Text, source.Text, target.Text, kind));
        await c.Edit(existing is null ? "AddVersion" : "Edit", form, () => { Read(); }, () =>
        {
            var version = Read();
            var current = c.Session.State!.Libraries.Single(l => l.Id == library.Id);
            var updated = current with { Versions = current.Versions.Where(v => v.Id != version.Id).Append(version).ToArray() };
            c.Session.Save(c.Session.State with { Libraries = c.Session.State.Libraries.Select(l => l.Id == library.Id ? updated : l).ToArray() });
            return Task.CompletedTask;
        }, [label, source, target, chooseFolder, chooseFile]);
    }
}
