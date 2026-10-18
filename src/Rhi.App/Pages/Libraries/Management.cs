using Microsoft.UI.Xaml.Controls;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.Core.Models;

namespace Rhi.App.Pages;

public sealed partial class LibrariesView
{
    private void AddReferenceNotice(StackPanel panel, Guid libraryId)
    {
        var state = c.Session.State!;
        var games = state.Receipts.Where(r => r.LibraryId == libraryId)
            .Select(r => state.Games.Single(g => g.Id == r.GameId).Name).Distinct().ToArray();
        if (games.Length > 0) panel.Children.Add(Ui.Text(Strings.Format("ReferenceNotice", string.Join(", ", games))));
    }

    private async Task RemoveLibrary(ResourceLibrary library)
    {
        if (!await c.Confirm("RemoveLibrary", Ui.Stack(Ui.Text(library.Name, 20), Ui.Label("RemoveConfigurationHelp")), "RemoveLibrary")) return;
        var state = c.Session.State!;
        c.Session.Save(state with { Libraries = state.Libraries.Where(l => l.Id != library.Id).ToArray() });
    }

    private async Task RemoveVersion(ResourceLibrary library, ResourceVersion version)
    {
        if (!await c.Confirm("RemoveVersion", Ui.Stack(Ui.Text(library.Name + " · " + version.Label, 20), Ui.Label("RemoveConfigurationHelp")), "RemoveVersion")) return;
        var state = c.Session.State!;
        c.Session.Save(state with { Libraries = state.Libraries.Select(l => l.Id == library.Id
            ? l with { Versions = l.Versions.Where(v => v.Id != version.Id).ToArray() } : l).ToArray() });
    }
}
