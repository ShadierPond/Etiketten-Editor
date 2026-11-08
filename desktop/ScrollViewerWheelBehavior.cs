using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LabelEditor.Desktop;

internal static class ScrollViewerWheelBehavior
{
    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(HandlePreviewMouseWheel),
            true);
    }

    public static void ScrollBy(ScrollViewer viewer, bool horizontal, double distance)
    {
        if (distance == 0)
            return;

        var current = horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset;
        var maximum = horizontal
            ? viewer.ExtentWidth - viewer.ViewportWidth
            : viewer.ExtentHeight - viewer.ViewportHeight;
        if (maximum <= 0)
            return;

        var target = Math.Clamp(current + distance, 0, maximum);
        if (horizontal)
            viewer.ScrollToHorizontalOffset(target);
        else
            viewer.ScrollToVerticalOffset(target);
    }

    private static void HandlePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || (Keyboard.Modifiers & ModifierKeys.Control) != 0 || e.Delta == 0)
            return;

        var horizontal = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var target = FindScrollableViewer(e.OriginalSource as DependencyObject, horizontal, e.Delta);
        if (target is null && horizontal)
        {
            horizontal = false;
            target = FindScrollableViewer(e.OriginalSource as DependencyObject, horizontal, e.Delta);
        }
        if (target is null || !ReferenceEquals(sender, target))
            return;

        ScrollBy(target, horizontal, -e.Delta * 0.8);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollableViewer(DependencyObject? source, bool horizontal, int delta)
    {
        while (source is not null)
        {
            if (source is ScrollViewer viewer)
            {
                var offset = horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset;
                var maximum = horizontal
                    ? viewer.ExtentWidth - viewer.ViewportWidth
                    : viewer.ExtentHeight - viewer.ViewportHeight;
                var canScroll = delta > 0 ? offset > 0 : offset < maximum;
                if (maximum > 0 && canScroll)
                    return viewer;
            }

            source = GetParent(source);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        if (element is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var parent = VisualTreeHelper.GetParent(element);
            if (parent is not null)
                return parent;
        }

        return LogicalTreeHelper.GetParent(element);
    }

}
