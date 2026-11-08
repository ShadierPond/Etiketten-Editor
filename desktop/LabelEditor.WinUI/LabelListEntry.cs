using Microsoft.UI.Xaml.Media.Imaging;

namespace LabelEditor.WinUI;

public sealed class LabelListEntry(string displayText, BitmapImage? photo)
{
    public string DisplayText { get; } = displayText;
    public BitmapImage? Photo { get; } = photo;
}
