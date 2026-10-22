using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Configuration;
using Rhi.Core.Storage;

namespace Rhi.App.Pages;

public sealed partial class LibrariesView : UserControl
{
    private readonly PageContext c;
    private Action? refreshData;
    public void RefreshData() => refreshData?.Invoke();
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
        string? detailStamp = null;
        void ShowLibrary(ResourceLibrary? library)
        {
            var stamp = AtomicJson.Fingerprint(new { Library = library, c.Session.State!.Receipts,
                c.Session.State.Games, Pending = c.Session.Store!.Pending(),
                Sources = library?.Versions.Select(ResourcePresentation.SourceAvailable).ToArray() });
            if (detail.Content is not null && c.LibraryId == library?.Id && detailStamp == stamp) return;
            detailStamp = stamp;
            c.LibraryId = library?.Id;
            detail.Content = LibraryBody(library);
        }
        void Filter()
        {
            var state = c.Session.State!;
            c.LibraryQuery = search.Text;
            var filtered = state.Libraries.Where(l => l.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            var library = filtered.FirstOrDefault(l => l.Id == c.LibraryId) ?? filtered.FirstOrDefault();
            filtering = true;
            if (list.ItemsSource is not ResourceLibrary[] shown || !shown.SequenceEqual(filtered)) list.ItemsSource = filtered;
            if (!Equals(list.SelectedItem, library)) list.SelectedItem = library;
            filtering = false;
            empty.Text = Strings.Get(state.Libraries.Length == 0 ? "NoLibraries" : "NoMatches");
            empty.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowLibrary(library);
        }
        refreshData = Filter;
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
        var right = Ui.Stack(Ui.Label("Libraries", 28));
        var locked = selected is not null && c.Session.Store!.Pending().Any(j => j.LibraryId == selected.Id);
        if (selected is null) right.Children.Add(Ui.Label(state.Libraries.Length == 0 ? "LibraryExplanation" : "NoMatches"));
        else
        {
            right.Children.Add(Ui.Text(selected.Name, 23));
            right.Children.Add(Ui.Text(ResourcePresentation.TargetLocation(selected.TargetRelative)));
            if (selected.SourceRoot is { Length: > 0 }) right.Children.Add(Ui.Secondary(Strings.Get("LibrarySourceRoot") + ": " + selected.SourceRoot));
            var editLibrary = c.Button("Edit", () => EditLibrary(selected), !c.Session.Busy && !locked);
            var addVersion = c.Button("AddVersion", () => EditVersion(selected), !c.Session.Busy && !locked);
            AutomationProperties.SetAutomationId(editLibrary, "LibraryEdit");
            AutomationProperties.SetAutomationId(addVersion, "LibraryAddVersion");
            var removeLibrary = c.Button("RemoveLibrary", () => RemoveLibrary(selected),
                !c.Session.Busy && !locked && !state.Receipts.Any(r => r.LibraryId == selected.Id));
            AutomationProperties.SetAutomationId(removeLibrary, "LibraryRemove");
            var relocate = c.Button("RelocateLibrary", () => RelocateLibrary(selected), !c.Session.Busy && !locked);
            AutomationProperties.SetAutomationId(relocate, "LibraryRelocate");
            right.Children.Add(Ui.HeaderActions("LibraryHeaderActions", editLibrary, addVersion, removeLibrary,
                c.FolderAction("OpenLibraryFolder", selected.SourceRoot, "LibraryOpenFolder"), relocate));
            AddReferenceNotice(right, selected.Id);
            right.Children.Add(Ui.Label("Versions", 20));
            var table = new StackPanel();
            table.Children.Add(Ui.TableLine(Ui.ResourceTableRow(true, false, Ui.Label("VersionLabel"), Ui.Label("SourcePath"), Ui.Label("Status"), Ui.Label("Actions")), true));
            foreach (var version in selected.Versions)
            {
                var references = state.Receipts.Where(r => r.LibraryId == selected.Id && r.VersionId == version.Id)
                    .Select(r => state.Games.First(g => g.Id == r.GameId).Name);
                var available = ResourcePresentation.SourceAvailable(version);
                var edit = c.Button("Edit", () => EditVersion(selected, version), !c.Session.Busy && !locked);
                AutomationProperties.SetAutomationId(edit, "VersionEdit_" + version.Id.ToString("N"));
                var name = new HyperlinkButton { Content = Ui.OneLine(ResourcePresentation.VersionName(version)), Padding = new Thickness(0), IsEnabled = !c.Session.Busy,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                ToolTipService.SetToolTip(name, ResourcePresentation.VersionName(version));
                AutomationProperties.SetName(name, Strings.Get("View"));
                AutomationProperties.SetAutomationId(name, "VersionView_" + version.Id.ToString("N"));
                name.Click += async (_, _) => await c.Run(() => ShowVersion(selected, version));
                var remove = c.Button("RemoveVersion", () => RemoveVersion(selected, version),
                    !c.Session.Busy && !locked && !state.Receipts.Any(r => r.LibraryId == selected.Id && r.VersionId == version.Id));
                AutomationProperties.SetAutomationId(remove, "VersionRemove_" + version.Id.ToString("N"));
                var status = Ui.Stack(Ui.Status(available ? "Available" : "SourceUnavailable", true), Ui.OneLine(string.Join(", ", references), true));
                status.Spacing = 4;
                table.Children.Add(Ui.TableLine(Ui.ResourceTableRow(false, false, name, Ui.OneLine(version.SourcePath),
                    status, Ui.CompactActions(edit, remove, c.FolderAction("OpenResourceFolder", version.SourcePath,
                        "VersionOpenFolder_" + version.Id.ToString("N"), sourceFile: true)))));
            }
            right.Children.Add(table);
        }
        var scroll = Ui.Scroll(right);
        AutomationProperties.SetAutomationId(scroll, "LibraryScroll");
        return Ui.Dock(scroll, Ui.Label(locked ? "PendingConfigurationLocked" : "ConfigOnly"));
    }

    private async Task EditLibrary(ResourceLibrary? existing)
    {
        if (existing is null && !await c.EnsureLibraryRoot()) return;
        var name = Ui.Input("LibraryName", existing?.Name ?? "");
        var target = Ui.Input("TargetRelative", existing?.TargetRelative ?? "");
        var sourceRoot = Ui.Input("LibrarySourceRoot", existing?.SourceRoot ?? "");
        AutomationProperties.SetAutomationId(sourceRoot, "LibrarySourceRootInput");
        var browse = c.Button("BrowseFolder", async () =>
        {
            var path = await c.Windows.PickFolder(Directory.Exists(sourceRoot.Text) ? sourceRoot.Text :
                Directory.Exists(c.Session.State!.Preferences.LibraryRoot) ? c.Session.State.Preferences.LibraryRoot : null);
            if (path is not null) sourceRoot.Text = path;
        });
        AutomationProperties.SetAutomationId(browse, "LibraryChooseFolder");
        browse.HorizontalAlignment = HorizontalAlignment.Stretch;
        target.PlaceholderText = Strings.Get("LibraryTargetPlaceholder");
        AutomationProperties.SetAutomationId(name, "LibraryNameInput");
        AutomationProperties.SetAutomationId(target, "LibraryTargetInput");
        var id = existing?.Id ?? Guid.NewGuid();
        ResourceLibrary Read() => ConfigurationRules.Library(c.Session.State!, c.Session.Store!.Root,
            new(id, name.Text, target.Text, existing?.Versions ?? [], sourceRoot.Text));
        await c.Edit(existing is null ? "AddLibrary" : "Edit",
            Ui.Stack(name, Ui.Stack(sourceRoot, browse, Ui.Label("LibrarySourceRootHelp")),
                Ui.Stack(target, Ui.Label("LibraryTargetHelp")), Ui.Label("ConfigOnly")),
            () => { Read(); }, () =>
            {
                var library = Read(); var state = c.Session.State!;
                c.Session.Save(state with { Libraries = state.Libraries.Where(l => l.Id != library.Id).Append(library).ToArray() });
                c.LibraryId = library.Id;
                c.Refresh();
                return Task.CompletedTask;
            }, [name, sourceRoot, target, browse]);
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
        string? StartFolder() => string.IsNullOrWhiteSpace(library.SourceRoot) ? null : library.SourceRoot;
        var chooseFolder = c.Button("ChooseFolder", async () => { var p = await c.Windows.PickFolder(StartFolder()); if (p is not null) source.Text = p; });
        AutomationProperties.SetAutomationId(chooseFolder, "VersionChooseFolder");
        var chooseFile = c.Button("ChooseFile", async () => { var p = await c.Windows.PickFile(initialDirectory: StartFolder()); if (p is not null) source.Text = p; });
        AutomationProperties.SetAutomationId(chooseFile, "VersionChooseFile");
        chooseFolder.IsEnabled = ordinary;
        target.IsEnabled = ordinary;
        if (!ordinary) target.Text = "";
        var sourceGroup = Ui.Stack(source, Ui.Row(chooseFolder, chooseFile), Ui.Label(ordinary ? "VersionSourceHelp" : "ReShadePending"));
        var destination = Ui.Text("");
        AutomationProperties.SetAutomationId(destination, "VersionTargetHint");
        if (ordinary)
        {
            void UpdateDestination() => destination.Text = ResourcePresentation.VersionDestination(target.Text, library.TargetRelative);
            target.TextChanged += (_, _) => UpdateDestination();
            UpdateDestination();
        }
        var targetGroup = ordinary
            ? Ui.Stack(target, Ui.Label("VersionTargetRule"), destination)
            : Ui.Stack(target, Ui.Label("ReShadeTargetMustBeRoot"));
        sourceGroup.Spacing = 6;
        targetGroup.Spacing = 6;
        var form = Ui.Stack(label, sourceGroup, targetGroup, Ui.Label("ConfigOnly"));
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
