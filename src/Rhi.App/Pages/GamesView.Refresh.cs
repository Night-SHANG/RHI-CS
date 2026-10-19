using Microsoft.UI.Xaml.Controls;
using Rhi.App.Presentation;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.App.Pages;

public sealed partial class GamesView
{
    private Action? refreshData;
    private string? gameStamp;
    private string? pendingStamp;
    private readonly List<Action> rowRefreshers = [];
    private readonly Dictionary<Guid, Guid> versionChoices = [];

    public void RefreshData()
    {
        var pending = AtomicJson.Fingerprint(c.Session.Store!.Pending());
        if (pendingStamp != pending) { pendingStamp = pending; gameStamp = null; }
        refreshData?.Invoke();
    }

    private Border RetainedResourceRow(GameEntry game, ResourceLibrary library, bool locked)
    {
        DeploymentReceipt? Receipt() => c.Session.State!.Receipts.SingleOrDefault(r => r.GameId == game.Id && r.LibraryId == library.Id);
        string Stamp() => AtomicJson.Fingerprint(new
        {
            Receipt = Receipt(),
            Observation = c.Session.TargetObservation?.GameId == game.Id && c.Session.TargetObservation.Revision == c.Session.State!.Revision
                ? c.Session.TargetObservation.Receipts.FirstOrDefault(r => r.LibraryId == library.Id) : null,
            Sources = library.Versions.Select(SourceAvailable).ToArray()
        });
        var receiptStamp = AtomicJson.Fingerprint(Receipt());
        var stamp = Stamp();
        var host = Ui.TableLine(ResourceRow(game, library, locked));
        var check = selectionBoxes[^1];
        rowRefreshers.Add(() =>
        {
            var next = Stamp();
            if (stamp == next) return;
            stamp = next;
            var nextReceipt = AtomicJson.Fingerprint(Receipt());
            if (receiptStamp != nextReceipt)
            {
                receiptStamp = nextReceipt;
                versionChoices.Remove(library.Id);
            }
            selectionBoxes.Remove(check);
            host.Child = ResourceRow(game, library, locked);
            check = selectionBoxes[^1];
        });
        return host;
    }
}
