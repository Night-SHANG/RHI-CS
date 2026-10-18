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
        var panel = Ui.Stack(Ui.Text(library.Name + " · " + ResourcePresentation.VersionName(version), 20),
            Ui.Text(Strings.Get("SourcePath") + ": " + version.SourcePath),
            Ui.Text(ResourcePresentation.TargetLocation(version.Kind == ResourceKind.Files
                ? version.TargetOverride ?? library.TargetRelative : "")),
            c.FolderAction("OpenResourceFolder", version.SourcePath, "VersionOpenFolder", sourceFile: true));
        if (referenced) AddReferenceNotice(panel, library.Id);
        if (locked) panel.Children.Add(Ui.Label("PendingConfigurationLocked"));
        var result = await c.Choose("VersionDetails", panel, "Edit", "RemoveVersion", "Close",
            !c.Session.Busy && !locked, !c.Session.Busy && !locked && !referenced, "VersionEdit", "VersionRemove");
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) await EditVersion(library, version);
        else if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary) await RemoveVersion(library, version);
    }
}
