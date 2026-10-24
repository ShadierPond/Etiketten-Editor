using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace FuniturenEditor.Desktop;

internal static class PrintPreviewViewerConfiguration
{
    public static void RemoveUnusedToolbarControls(DocumentViewer viewer)
    {
        viewer.ApplyTemplate();
        if (viewer.Template?.FindName("PART_FindToolBarHost", viewer) is UIElement findToolbar)
            findToolbar.Visibility = Visibility.Collapsed;

        foreach (var button in Descendants(viewer).OfType<ButtonBase>())
        {
            if (button.Command == ApplicationCommands.Print ||
                button.Command == ApplicationCommands.Copy ||
                IsTwoPageViewButton(button))
            {
                button.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static bool IsTwoPageViewButton(ButtonBase button)
    {
        var commandName = button.Command is RoutedUICommand command
            ? $"{command.Name} {command.Text}"
            : button.Command?.ToString() ?? "";
        var accessibleName = AutomationProperties.GetName(button);
        var description = string.Join(" ", commandName, accessibleName, button.ToolTip?.ToString(),
            (button as ContentControl)?.Content?.ToString());
        var compactDescription = new string(description
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        return compactDescription.Contains("twopage", StringComparison.Ordinal) ||
               compactDescription.Contains("2page", StringComparison.Ordinal) ||
               compactDescription.Contains("zweiseite", StringComparison.Ordinal) ||
               compactDescription.Contains("2seite", StringComparison.Ordinal) ||
               description.Contains("two page", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("two-page", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("2 page", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("zwei seite", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("2 seite", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
