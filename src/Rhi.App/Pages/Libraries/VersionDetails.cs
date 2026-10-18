using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Models;

namespace Rhi.App.Pages;

public sealed partial class LibrariesView
{
    private async Task ShowVersion(ResourceLibrary library, ResourceVersion version)
    {
        var locked = c.Session.Store!.Pending().Any(j => j.LibraryId == library.Id);
        var referenced = c.Session.State!.Receipts.Any(r => r.LibraryId == library.Id && r.VersionId == version.Id);
        var next = "";
        Action close = () => { };
        var edit = c.Action("Edit", () => { next = "edit"; close(); }, !c.Session.Busy && !locked);
        var remove = c.Action("RemoveVersion", () => { next = "remove"; close(); }, !c.Session.Busy && !locked && !referenced);
        AutomationProperties.SetAutomationId(edit, "VersionEdit");
        AutomationProperties.SetAutomationId(remove, "VersionRemove");
        var panel = Ui.Stack(Ui.Text(library.Name + " · " + ResourcePresentation.VersionName(version), 20),
            Ui.Text(Strings.Get("SourcePath") + ": " + version.SourcePath),
            Ui.Text(ResourcePresentation.TargetLocation(version.Kind == ResourceKind.Files
                ? version.TargetOverride ?? library.TargetRelative : "")), Ui.Row(edit, remove));
        if (referenced) AddReferenceNotice(panel, library.Id);
        if (locked) panel.Children.Add(Ui.Label("PendingConfigurationLocked"));
        await c.Show("VersionDetails", panel, dialog => close = dialog.Hide);
        if (next == "edit") await EditVersion(library, version);
        else if (next == "remove") await RemoveVersion(library, version);
    }
}
