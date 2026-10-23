using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    private void TrackDialogSize(ContentDialog dialog, ScrollViewer scroll, bool stableContent = false,
        FrameworkElement? feedback = null)
    {
        void Resize()
        {
            scroll.Width = Math.Clamp(Root.ActualWidth - 96, 120, 480);
            var feedbackHeight = 0.0;
            if (feedback is not null)
            {
                feedback.Width = scroll.Width;
                if (feedback.Visibility == Visibility.Visible)
                {
                    feedback.Measure(new Windows.Foundation.Size(scroll.Width, double.PositiveInfinity));
                    feedbackHeight = feedback.DesiredSize.Height;
                }
            }
            scroll.MaxHeight = Math.Max(64, Root.ActualHeight - 180 - feedbackHeight);
            scroll.Height = stableContent ? Math.Min(scroll.MaxHeight, 480) : double.NaN;
        }
        void Changed(object sender, SizeChangedEventArgs args) => Resize();
        dialog.Opened += (_, _) =>
        {
            // Let WinUI measure current content, including wrapping and validation.
            // Freezing an early DesiredSize clips controls after their templates load.
            Resize();
            Root.SizeChanged += Changed;
            if (feedback is not null) feedback.SizeChanged += Changed;
        };
        dialog.Closed += (_, _) =>
        {
            Root.SizeChanged -= Changed;
            if (feedback is not null) feedback.SizeChanged -= Changed;
        };
        Resize();
    }
}
