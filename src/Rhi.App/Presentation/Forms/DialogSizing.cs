using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    private void TrackDialogSize(ContentDialog dialog, ScrollViewer scroll, FrameworkElement? feedback = null)
    {
        var preferredHeight = 360d;
        if (feedback is not null && scroll.Content is FrameworkElement fields)
        {
            fields.Measure(new Windows.Foundation.Size(scroll.Width, double.PositiveInfinity));
            preferredHeight = Math.Clamp(fields.DesiredSize.Height, 64, 360);
        }
        void Resize()
        {
            scroll.Width = Math.Clamp(Root.ActualWidth - 96, 120, 480);
            scroll.MaxHeight = Math.Clamp(Root.ActualHeight - (feedback is null ? 220 : 324), 64, 480);
            if (feedback is not null)
            {
                scroll.Height = Math.Min(scroll.MaxHeight, preferredHeight);
                feedback.Width = scroll.Width;
            }
        }
        void Changed(object sender, SizeChangedEventArgs args) => Resize();
        dialog.Opened += (_, _) => { Resize(); Root.SizeChanged += Changed; };
        dialog.Closed += (_, _) => Root.SizeChanged -= Changed;
        Resize();
    }
}
