using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    private static void ClipFormViewport(ScrollViewer scroll)
    {
        // WinUI's UIA peer can include descendants beyond the native
        // ScrollContentPresenter clip. Expose the actual arranged viewport,
        // not the bounds of partially scrolled-out fields, to accessibility.
        var clip = new RectangleGeometry();
        scroll.Clip = clip;
        void UpdateClip(object? sender, object args)
        {
            // RenderSize can exceed the allocated slot after layout squeezing.
            // Slot changes do not necessarily raise SizeChanged.
            var slot = LayoutInformation.GetLayoutSlot(scroll);
            var bounds = new Rect(0, 0, Math.Max(0, Math.Min(scroll.ActualWidth, slot.Width)),
                Math.Max(0, Math.Min(scroll.ActualHeight, slot.Height)));
            if (clip.Rect != bounds) clip.Rect = bounds;
        }
        scroll.Loaded += (_, _) =>
        {
            scroll.LayoutUpdated += UpdateClip;
            UpdateClip(null, EventArgs.Empty);
        };
        scroll.Unloaded += (_, _) => scroll.LayoutUpdated -= UpdateClip;
    }
}
