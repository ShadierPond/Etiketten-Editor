using Microsoft.UI.Xaml.Media.Imaging;

namespace FuniturenEditor_WinUI;

public sealed class FurnitureListEntry(string displayText, BitmapImage? photo)
{
    public string DisplayText { get; } = displayText;
    public BitmapImage? Photo { get; } = photo;
}
