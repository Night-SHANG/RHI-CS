using Microsoft.UI.Xaml;
using Rhi.App.Localization;
using Rhi.App.Presentation;
using Rhi.App.Services;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.App;

public sealed partial class MainWindow
{
    private string TaskHeading()
    {
        if (session.TaskContext is not { } task) return "";
        var actions = task.Resources.Select(r => r.Action).Distinct().ToArray();
        return task.GameName + " · " + Strings.Get(actions.Length == 1 ? actions[0] : "BatchOperation");
    }

    private string ProgressText(TaskProgress progress) => TaskHeading() + " · " +
        Strings.Format("TaskProgress", Strings.Get(progress.Phase), progress.Library, progress.Completed, progress.Total);

    private async void Result_Click(object sender, RoutedEventArgs e) => await context.Run(async () =>
    {
        var panel = Ui.Stack(Ui.Text(TaskHeading(), 20), Ui.Text(session.TaskContext?.TargetRoot ?? ""));
        foreach (var result in session.Results)
        {
            var item = session.TaskContext?.Resources.FirstOrDefault(r => r.LibraryId == result.LibraryId);
            var row = Ui.Stack(Ui.Text((item?.Name ?? result.LibraryId.ToString()) +
                (string.IsNullOrEmpty(item?.Version) ? "" : " · " + item.Version), 18),
                Ui.Text(Strings.Get(item?.Action ?? "Unknown") + " · " + Strings.Get(result.Status.ToString())));
            if (Strings.Get(result.Detail) != Strings.Get(result.Status.ToString()))
                row.Children.Add(Ui.Text(Strings.Get(result.Detail)));
            if (session.Store is { } store && result.OperationId is { } operation)
            {
                var copies = ProtectionCatalog.Find(store, operation)?.Directory;
                if (copies is not null && Directory.Exists(copies))
                    row.Children.Add(context.Action("OpenProtectedCopies", () => WindowsIntegration.OpenFolder(copies)));
            }
            panel.Children.Add(Ui.Card(row));
        }
        await context.Show("TaskResults", panel);
    });
}
