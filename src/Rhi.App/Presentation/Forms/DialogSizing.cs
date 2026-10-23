using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    private void TrackDialogSize(ContentDialog dialog, ScrollViewer scroll, bool stableContent = false,
        FrameworkElement? container = null)
    {
        void Resize()
        {
            var width = Math.Clamp(Root.ActualWidth - 96, 120, 480);
            var height = Math.Max(64, Root.ActualHeight - 180);
            scroll.Width = width;
            if (container is not null)
            {
                // Grid measures the Auto feedback row first and constrains the
                // scrolling star row in the same layout pass. Do not resize a
                // sibling from SizeChanged or freeze an early DesiredSize.
                container.Width = width;
                container.MaxHeight = height;
                scroll.MaxHeight = double.PositiveInfinity;
                scroll.Height = double.NaN;
            }
            else
            {
                scroll.MaxHeight = height;
                scroll.Height = stableContent ? Math.Min(height, 480) : double.NaN;
            }
        }
        void Changed(object sender, SizeChangedEventArgs args) => Resize();
        dialog.Opened += (_, _) =>
        {
            // Let WinUI measure current content, including wrapping and validation.
            // Freezing an early DesiredSize clips controls after their templates load.
            Resize();
            Root.SizeChanged += Changed;
        };
        dialog.Closed += (_, _) =>
        {
            Root.SizeChanged -= Changed;
        };
        Resize();
    }
}
