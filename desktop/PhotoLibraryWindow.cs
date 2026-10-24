using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FuniturenEditor.Desktop;

public sealed class PhotoLibraryWindow : Window
{
    private readonly CatalogData catalog;
    private readonly PhotoLibrary photoLibrary;
    private readonly WrapPanel gallery = new() { Margin = new Thickness(2) };
    private readonly TextBlock status = new() { Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134)) };
    private Border? selectedCard;

    public string? SelectedPhotoKey { get; private set; }
    public bool HasChanges { get; private set; }

    public PhotoLibraryWindow(CatalogData catalog, PhotoLibrary photoLibrary)
    {
        this.catalog = catalog;
        this.photoLibrary = photoLibrary;
        Title = "Foto-Mediathek";
        Width = 760;
        Height = 560;
        MinWidth = 520;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        var layout = new DockPanel { Margin = new Thickness(20) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Foto auswählen",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 43, 66))
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Importierte und hinzugefügte Fotos werden lokal gespeichert und können wiederverwendet werden.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);

        var actions = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var addButton = new Button { Content = "Fotos hinzufügen", HorizontalAlignment = HorizontalAlignment.Left };
        addButton.Click += AddPhotos_Click;
        DockPanel.SetDock(addButton, Dock.Left);
        actions.Children.Add(addButton);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(12, 0, 0, 0);
        actions.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelButton = new Button { Content = "Abbrechen", IsCancel = true };
        var useButton = new Button { Content = "Foto verwenden", IsDefault = true, IsEnabled = false, Margin = new Thickness(0) };
        cancelButton.Click += (_, _) => DialogResult = false;
        useButton.Click += (_, _) => UseSelectedPhoto();
        gallery.Tag = useButton;
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(useButton);
        DockPanel.SetDock(buttons, Dock.Right);
        actions.Children.Add(buttons);
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);

        var galleryScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = gallery
        };
        layout.Children.Add(galleryScroll);
        Content = layout;
        Loaded += (_, _) => RefreshGallery();
    }

    private void AddPhotos_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Fotos zur Mediathek hinzufügen",
            Filter = "Bilddateien|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            foreach (var file in dialog.FileNames)
                SelectedPhotoKey = photoLibrary.AddPhoto(file, catalog);
            HasChanges = true;
            RefreshGallery();
            SelectCard(SelectedPhotoKey);
            status.Text = $"{dialog.FileNames.Length} Foto(s) hinzugefügt. Auswahl mit „Foto verwenden“ bestätigen.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Die Fotos konnten nicht hinzugefügt werden.\n\n{ex.Message}",
                "Foto-Mediathek", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshGallery()
    {
        gallery.Children.Clear();
        selectedCard = null;
        var useButton = (Button)gallery.Tag;
        useButton.IsEnabled = false;
        var added = 0;

        foreach (var (key, fileName) in catalog.Library.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var path = photoLibrary.GetPhotoPath(catalog, key);
            if (path is null)
                continue;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 150;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();

            var cardContent = new StackPanel();
            cardContent.Children.Add(new Image { Source = image, Width = 144, Height = 112, Stretch = Stretch.Uniform });
            cardContent.Children.Add(new TextBlock
            {
                Text = key,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, 5, 2, 3),
                ToolTip = fileName
            });
            var deleteContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            deleteContent.Children.Add(new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 5, 0)
            });
            deleteContent.Children.Add(new TextBlock { Text = "Foto löschen" });

            var deleteButton = new Button
            {
                Content = deleteContent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(4, 2, 4, 2),
                ToolTip = "Foto dauerhaft aus der lokalen Mediathek entfernen"
            };
            AutomationProperties.SetName(deleteButton, "Foto löschen");
            deleteButton.Click += (_, _) => DeletePhoto(key);
            cardContent.Children.Add(deleteButton);

            var card = new Border
            {
                Width = 170,
                MinHeight = 190,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(216, 224, 232)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Tag = key,
                Child = cardContent
            };
            card.MouseLeftButtonUp += (_, _) => SelectCard(key);
            card.MouseLeftButtonDown += (_, args) =>
            {
                if (args.ClickCount == 2)
                    UsePhoto(key);
            };
            gallery.Children.Add(card);
            if (key == SelectedPhotoKey)
                SelectCard(key);
            added++;
        }

        status.Text = added == 0 ? "Die Mediathek ist leer. Fügen Sie Fotos hinzu oder importieren Sie einen JSON-Katalog mit Fotos." : $"{added} Foto(s) in der lokalen Mediathek";
    }

    private void DeletePhoto(string key)
    {
        var references = CatalogData.GetPhotoReferences(catalog, key);
        var message = references.Count == 0
            ? $"Das Foto „{key}“ wird dauerhaft aus der lokalen Mediathek gelöscht."
            : $"Das Foto wird von {references.Count} Artikel(n) verwendet. Beim Löschen werden deren Fotofelder geleert:\n\n" +
              string.Join("\n", references.Take(8).Select(reference =>
                  $"{reference.Category} – {PhotoReferenceTitle(reference)} (Position {reference.Position})")) +
              (references.Count > 8 ? $"\n… und {references.Count - 8} weitere" : "");
        if (MessageBox.Show(this, message, "Foto löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var removedReferences = photoLibrary.RemovePhoto(catalog, key);
            if (SelectedPhotoKey == key)
                SelectedPhotoKey = null;
            HasChanges = true;
            RefreshGallery();
            status.Text = removedReferences == 0
                ? "Foto wurde aus der Mediathek gelöscht."
                : $"Foto gelöscht; Fotofelder von {removedReferences} Artikel(n) wurden geleert.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Das Foto konnte nicht gelöscht werden.\n\n{ex.Message}",
                "Foto-Mediathek", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string PhotoReferenceTitle(PhotoReference reference) =>
        string.IsNullOrWhiteSpace(reference.Title) ? "(ohne Namen)" : reference.Title.Trim();

    private void SelectCard(string? key)
    {
        if (key is null)
            return;

        if (selectedCard is not null)
        {
            selectedCard.BorderBrush = new SolidColorBrush(Color.FromRgb(216, 224, 232));
            selectedCard.BorderThickness = new Thickness(1);
        }

        selectedCard = gallery.Children.OfType<Border>().FirstOrDefault(card => Equals(card.Tag, key));
        if (selectedCard is null)
            return;
        SelectedPhotoKey = key;
        selectedCard.BorderBrush = new SolidColorBrush(Color.FromRgb(36, 100, 158));
        selectedCard.BorderThickness = new Thickness(2);
        ((Button)gallery.Tag).IsEnabled = true;
    }

    private void UseSelectedPhoto()
    {
        if (SelectedPhotoKey is not null)
            UsePhoto(SelectedPhotoKey);
    }

    private void UsePhoto(string key)
    {
        SelectedPhotoKey = key;
        DialogResult = true;
    }
}
