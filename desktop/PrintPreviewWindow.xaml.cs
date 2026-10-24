using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FuniturenEditor.Desktop;

public partial class PrintPreviewWindow : Window
{
    private ScrollViewer? previewScrollViewer;
    private HwndSource? windowSource;

    public PrintPreviewWindow(FixedDocument document, string title)
    {
        InitializeComponent();
        DocumentTitleText.Text = title;
        PreviewViewer.Document = document;
        SourceInitialized += PrintPreviewWindow_SourceInitialized;
        Closed += PrintPreviewWindow_Closed;
    }

    private void PreviewViewer_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            PrintPreviewViewerConfiguration.RemoveUnusedToolbarControls(PreviewViewer);
            previewScrollViewer = FindVisualChild<ScrollViewer>(PreviewViewer);
            if (previewScrollViewer is null)
                return;

            previewScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            previewScrollViewer.PanningMode = PanningMode.Both;
        });
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void PrintPreviewWindow_SourceInitialized(object? sender, EventArgs e)
    {
        windowSource = (HwndSource?)PresentationSource.FromVisual(this);
        windowSource?.AddHook(PrintPreviewWindow_WindowMessage);
    }

    private void PrintPreviewWindow_Closed(object? sender, EventArgs e)
    {
        windowSource?.RemoveHook(PrintPreviewWindow_WindowMessage);
        windowSource = null;
    }

    private IntPtr PrintPreviewWindow_WindowMessage(
        IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmMouseHWheel = 0x020E;
        if (message != wmMouseHWheel || previewScrollViewer is null)
            return IntPtr.Zero;

        var screenPoint = new Point(
            (short)(lParam.ToInt64() & 0xFFFF),
            (short)((lParam.ToInt64() >> 16) & 0xFFFF));
        var viewerOrigin = PreviewViewer.PointToScreen(new Point());
        if (!new Rect(viewerOrigin, PreviewViewer.RenderSize).Contains(screenPoint))
            return IntPtr.Zero;

        var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        previewScrollViewer.ScrollToHorizontalOffset(
            previewScrollViewer.HorizontalOffset + delta * 0.5);
        handled = true;
        return IntPtr.Zero;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
                return descendant;
        }
        return null;
    }
}
