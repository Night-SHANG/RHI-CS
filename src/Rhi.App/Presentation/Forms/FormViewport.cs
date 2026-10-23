using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    private static Action ClipFormViewport(ScrollViewer scroll, Grid container)
    {
        // UIA can include descendants outside the native presenter clip.
        // Expose the visible arranged viewport without changing the form's layout.
        var clip = new RectangleGeometry();
        scroll.Clip = clip;
        var active = true;
        void UpdateClip(object? sender, object args)
        {
            if (!active || scroll.XamlRoot is null) return;
            var slot = LayoutInformation.GetLayoutSlot(scroll);
            var bounds = new Rect(0, 0, Math.Max(0, Math.Min(scroll.ActualWidth, slot.Width)),
                Math.Max(0, Math.Min(scroll.ActualHeight, slot.Height)));
            if (clip.Rect != bounds) clip.Rect = bounds;
        }
        EventHandler<object> layoutChanged = UpdateClip;
        void SizeChanged(object sender, SizeChangedEventArgs args)
        {
            UpdateClip(sender, args);
            scroll.DispatcherQueue.TryEnqueue(() => UpdateClip(null, EventArgs.Empty));
        }
        void Loaded(object sender, RoutedEventArgs args) => UpdateClip(sender, args);
        // ContentDialog reparenting can emit Unloaded while IsLoaded is still true.
        // Own these subscriptions for ShowAsync's lifetime, not tree attachment.
        scroll.LayoutUpdated += layoutChanged;
        scroll.SizeChanged += SizeChanged;
        container.SizeChanged += SizeChanged;
        scroll.Loaded += Loaded;
        return () =>
        {
            if (!active) return;
            active = false; // Already queued callbacks must not touch a closed form.
            scroll.LayoutUpdated -= layoutChanged;
            scroll.SizeChanged -= SizeChanged;
            container.SizeChanged -= SizeChanged;
            scroll.Loaded -= Loaded;
        };
    }
}
