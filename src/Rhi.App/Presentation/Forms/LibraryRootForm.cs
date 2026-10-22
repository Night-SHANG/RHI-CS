using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.Core.Configuration;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    public string? LibraryRootDraft { get; set; }

    public async Task<bool> EnsureLibraryRoot()
    {
        var saved = Session.State!.Preferences.LibraryRoot;
        if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved)) return true;
        var path = Ui.Input("LibraryRoot", saved ?? "");
        AutomationProperties.SetAutomationId(path, "FirstLibraryRootInput");
        var browse = Button("BrowseFolder", async () =>
        {
            var chosen = await Windows.PickFolder(Directory.Exists(path.Text) ? path.Text : null);
            if (chosen is not null) path.Text = chosen;
        });
        AutomationProperties.SetAutomationId(browse, "FirstLibraryRootBrowse");
        string Read() => ConfigurationRules.LibraryDirectory(Session.State!, Session.Store!.Root, path.Text);
        return await Edit("SetLibraryRoot", Ui.Stack(Ui.Label("LibraryRootFirstHelp"), Ui.DirectoryField(path, browse)),
            () => { Read(); }, () =>
            {
                var root = Read();
                Session.Save(Session.State! with { Preferences = Session.State!.Preferences with { LibraryRoot = root } });
                LibraryRootDraft = null;
                return Task.CompletedTask;
            }, [path, browse], "SaveAndContinue");
    }
}
