using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;
using Rhi.Core.Preparation;

namespace Rhi.App.Pages;

public sealed partial class LibrariesView
{
    private async Task PrepareOfflineReShade()
    {
        var installer = Ui.Input("OfflineInstaller");
        var parent = Ui.Input("OfflineOutputParent");
        var name = Ui.Input("OfflineFolderName", "ReShade-offline");
        var architecture = Ui.Choice("OfflineArchitecture", ["x64", "x86"]);
        var apis = new[] { "DirectX 10/11/12", "DirectX 9", "OpenGL" };
        var api = Ui.Choice("GraphicsApi", apis);
        AutomationProperties.SetAutomationId(installer, "OfflineInstallerInput");
        AutomationProperties.SetAutomationId(parent, "OfflineParentInput");
        AutomationProperties.SetAutomationId(name, "OfflineNameInput");
        AutomationProperties.SetAutomationId(architecture, "OfflineArchitectureChoice");
        AutomationProperties.SetAutomationId(api, "OfflineApiChoice");
        var file = c.Button("ChooseFile", async () => { var path = await c.Windows.PickFile(".exe"); if (path is not null) installer.Text = path; });
        var folder = c.Button("ChooseFolder", async () => { var path = await c.Windows.PickFolder(); if (path is not null) parent.Text = path; });
        OfflineReShadeRequest Read() => new(installer.Text, parent.Text, name.Text, architecture.SelectedIndex == 1 ? "x86" : "x64", apis[Math.Max(0, api.SelectedIndex)]);
        string? output = null;
        var saved = await c.Edit("MakeOfflineReShade", Ui.Stack(Ui.Label("OfflineHelp"), installer, file,
            Ui.Row(architecture, api), parent, folder, name),
            () => OfflineReShade.Validate(c.Session.State!, c.Session.Store!.Root, Read()),
            async () => { output = await c.Session.CreateOfflineReShade(Read()); },
            [installer, parent, name, architecture, api, file, folder], "CreateOffline");
        if (saved && output is not null)
            await c.Show("OfflineReady", Ui.Stack(Ui.Text(output), Ui.Label("OfflineNextStep"),
                c.Action("OpenFolder", () => WindowsIntegration.OpenFolder(output))));
    }
}
