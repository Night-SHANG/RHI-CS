using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    private void TrackDialogSize(ContentDialog dialog, ScrollViewer scroll, FrameworkElement? feedback = null, bool stableContent = false)
    {
        void Resize()
        {
            scroll.Width = Math.Clamp(Root.ActualWidth - 96, 120, 480);
            var feedbackHeight = feedback is { Visibility: Visibility.Visible } ? feedback.ActualHeight + 12 : 0;
            scroll.MaxHeight = Math.Max(64, Root.ActualHeight - (feedback is null ? 180 : 220) - feedbackHeight);
            if (feedback is not null)
            {
                // Auto height follows wrapped paths/native headers after edits and resize.
                // A one-time Opened measurement can be stale before templates finish layout.
                feedback.Width = scroll.Width;
            }
            else if (stableContent) scroll.Height = Math.Min(scroll.MaxHeight, 480);
        }
        void Changed(object sender, SizeChangedEventArgs args) => Resize();
        dialog.Opened += (_, _) =>
        {
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
