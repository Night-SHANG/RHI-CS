using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rhi.App.Localization;
using Rhi.App.Services;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    public Button FolderAction(string key, string? path, string id, bool sourceFile = false)
    {
        var available = !string.IsNullOrWhiteSpace(path) && (Directory.Exists(path) || sourceFile && File.Exists(path));
        var button = Action(key, () => WindowsIntegration.OpenFolder(sourceFile && File.Exists(path)
            ? Path.GetDirectoryName(Path.GetFullPath(path!))! : path!), available);
        AutomationProperties.SetAutomationId(button, id);
        ToolTipService.SetToolTip(button, available ? path : Strings.Get("FolderUnavailable"));
        return button;
    }
}
