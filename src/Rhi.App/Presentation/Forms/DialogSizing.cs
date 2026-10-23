using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    private void TrackDialogSize(ContentDialog dialog, ScrollViewer scroll, bool stableContent = false)
    {
        void Resize()
        {
            scroll.Width = Math.Clamp(Root.ActualWidth - 96, 120, 480);
            scroll.MaxHeight = Math.Max(64, Root.ActualHeight - 180);
            scroll.Height = stableContent ? Math.Min(scroll.MaxHeight, 480) : double.NaN;
        }
        void Changed(object sender, SizeChangedEventArgs args) => Resize();
        dialog.Opened += (_, _) =>
        {
            // Let WinUI measure current content, including wrapping and validation.
            // Freezing an early DesiredSize clips controls after their templates load.
            Resize();
            Root.SizeChanged += Changed;
        };
        dialog.Closed += (_, _) => Root.SizeChanged -= Changed;
        Resize();
    }
}
