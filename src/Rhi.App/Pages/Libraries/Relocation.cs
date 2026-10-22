using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Configuration;
using Rhi.Core.Models;

namespace Rhi.App.Pages;

public sealed partial class LibrariesView
{
    private async Task RelocateLibrary(ResourceLibrary library)
    {
        if (string.IsNullOrWhiteSpace(library.SourceRoot)) throw new DeploymentException("LibraryRelocationNoRoot");
        var root = Ui.Input("LibrarySourceRoot", library.SourceRoot);
        AutomationProperties.SetAutomationId(root, "RelocateLibraryInput");
        var browse = c.Button("BrowseFolder", async () =>
        {
            var initial = Directory.Exists(root.Text) ? root.Text : c.Session.State!.Preferences.LibraryRoot;
            var selected = await c.Windows.PickFolder(Directory.Exists(initial) ? initial : null);
            if (selected is not null) root.Text = selected;
        });
        AutomationProperties.SetAutomationId(browse, "RelocateLibraryBrowse");
        LibraryRelocationPlan? plan = null;
        if (!await c.Edit("RelocateLibrary", Ui.Stack(Ui.DirectoryField(root, browse), Ui.Label("LibraryRelocationHelp")),
            () => { ConfigurationRules.LibraryDirectory(c.Session.State!, c.Session.Store!.Root, root.Text); },
            () =>
            {
                plan = LibraryRelocation.Preview(c.Session.State!, c.Session.Store!.Root, library.Id, root.Text);
                return Task.CompletedTask;
            }, [root, browse], "ReviewMatches")) return;
        var panel = Ui.Stack(Ui.Text(library.Name, 20), Ui.Label("LibraryRelocationReview"));
        foreach (var match in plan!.Matches)
            panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text(match.Label, 16), Ui.Text(match.Before + "\n→ " + match.After),
                Ui.Label(match.Status))));
        if (await c.Confirm("RelocateLibrary", panel, "Save", configure: dialog =>
            AutomationProperties.SetAutomationId(dialog, "LibraryRelocationReview")))
            c.Session.Save(LibraryRelocation.Apply(c.Session.State!, c.Session.Store!.Root, plan));
    }
}
