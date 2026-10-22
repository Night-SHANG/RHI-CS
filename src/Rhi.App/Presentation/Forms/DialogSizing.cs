using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    private void TrackDialogSize(ContentDialog dialog, ScrollViewer scroll, FrameworkElement? feedback = null, bool stableContent = false)
    {
        var preferredHeight = 360d;
        void Resize()
        {
            scroll.Width = Math.Clamp(Root.ActualWidth - 96, 120, 480);
            scroll.MaxHeight = Math.Max(64, Root.ActualHeight - (feedback is null ? 180 : 264));
            if (feedback is not null)
            {
                scroll.Height = Math.Min(scroll.MaxHeight, preferredHeight);
                feedback.Width = scroll.Width;
            }
            else if (stableContent) scroll.Height = Math.Min(scroll.MaxHeight, 480);
        }
        void Changed(object sender, SizeChangedEventArgs args) => Resize();
        dialog.Opened += (_, _) =>
        {
            // Native input templates must be attached before their headers can be measured.
            if (feedback is not null && scroll.Content is FrameworkElement fields)
            {
                fields.UpdateLayout();
                fields.Measure(new Windows.Foundation.Size(scroll.Width, double.PositiveInfinity));
                preferredHeight = Math.Max(64, fields.DesiredSize.Height);
            }
            Resize();
            Root.SizeChanged += Changed;
        };
        dialog.Closed += (_, _) => Root.SizeChanged -= Changed;
        Resize();
    }
}
