using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LabelEditor.Desktop;

public sealed class PhotoLibraryWindow : Window
{
    private readonly CatalogData catalog;
    private readonly PhotoLibrary photoLibrary;
    private readonly Action saveChanges;
    private readonly ListBox photoList = new()
    {
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0)
    };
    private readonly Border searchBoxContainer = new()
    {
        Height = 38,
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(156, 182, 206)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBox searchBox = new()
    {
        FocusVisualStyle = null,
        MinHeight = 30,
        VerticalAlignment = VerticalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Padding = new Thickness(0, 5, 0, 5),
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        ToolTip = "Fotomediathek nach Dateiname oder Foto-ID durchsuchen"
    };
    private readonly TextBlock searchPlaceholder = new()
    {
        Text = "Suchen",
        Foreground = new SolidColorBrush(Color.FromRgb(124, 139, 153)),
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false
    };
    private readonly Button clearSearchButton = new()
    {
        Content = new System.Windows.Shapes.Path
        {
            Width = 11,
            Height = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse("M 2,2 L 9,9 M 9,2 L 2,9"),
            Stroke = new SolidColorBrush(Color.FromRgb(101, 117, 134)),
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        },
        Width = 26,
        Height = 26,
        MinHeight = 0,
        Padding = new Thickness(0),
        Margin = new Thickness(2, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134)),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        ToolTip = "Suche löschen",
        Visibility = Visibility.Collapsed
    };
    private readonly ComboBox sortPicker = new() { Width = 152, MinHeight = 30 };
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly TextBox previewName = new()
    {
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.NoWrap,
        Height = 32,
        Padding = new Thickness(7, 2, 7, 2),
        MinWidth = 160,
        MaxWidth = 280,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private ScrollViewer? photoListScrollViewer;
    private CancellationTokenSource? previewLoadCancellation;
    private readonly CancellationTokenSource thumbnailLoadCancellation = new();
    private readonly SemaphoreSlim previewLoadGate = new(1, 1);
    private readonly SemaphoreSlim thumbnailLoadGate = new(2, 2);
    private readonly Dictionary<string, BitmapImage> thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> thumbnailLoadsInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock previewEmpty = new()
    {
        Text = "Wähle links ein Foto aus.",
        Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock previewKey = new()
    {
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134)),
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly TextBlock previewAddedAt = new()
    {
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134))
    };
    private readonly TextBlock status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134)),
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly Button deleteButton = new()
    {
        Content = new TextBlock
        {
            Text = "\uE74D",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16
        },
        Width = 36,
        Height = 36,
        Padding = new Thickness(0),
        IsEnabled = false,
        ToolTip = "Foto dauerhaft aus der lokalen Mediathek entfernen"
    };
    private readonly Button useButton = new()
    {
        Content = "Foto verwenden",
        IsDefault = true,
        IsEnabled = false
    };
    private readonly Button renameButton = new()
    {
        Content = "Umbenennen",
        IsEnabled = false,
        Margin = new Thickness(8, 0, 0, 0),
        Width = 96,
        Height = 36,
        Padding = new Thickness(6, 2, 6, 2),
        VerticalAlignment = VerticalAlignment.Center
    };

    public string? SelectedPhotoKey { get; private set; }
    public bool HasChanges { get; private set; }

    public PhotoLibraryWindow(CatalogData catalog, PhotoLibrary photoLibrary, Action saveChanges)
    {
        this.catalog = catalog;
        this.photoLibrary = photoLibrary;
        this.saveChanges = saveChanges;
        Title = "Foto-Mediathek";
        Width = 920;
        Height = 640;
        MinWidth = 680;
        MinHeight = 460;
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

        var actions = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var addButton = new Button
        {
            Content = "Fotos hinzufügen",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        addButton.Click += AddPhotos_Click;
        Grid.SetColumn(addButton, 0);
        actions.Children.Add(addButton);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(12, 0, 12, 0);
        Grid.SetColumn(status, 1);
        actions.Children.Add(status);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancelButton = new Button { Content = "Abbrechen", IsCancel = true };
        cancelButton.Click += (_, _) => DialogResult = false;
        Grid.SetColumn(cancelButton, 2);
        actions.Children.Add(cancelButton);
        useButton.Margin = new Thickness(8, 0, 0, 0);
        useButton.Click += (_, _) => UseSelectedPhoto();
        buttons.Children.Add(useButton);
        Grid.SetColumn(buttons, 3);
        actions.Children.Add(buttons);
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);

        sortPicker.Items.Add("Neueste zuerst");
        sortPicker.Items.Add("Älteste zuerst");
        sortPicker.Items.Add("Alphabetisch");
        sortPicker.SelectedIndex = 0;
        sortPicker.SelectionChanged += (_, _) => RefreshGallery();
        deleteButton.Click += (_, _) =>
        {
            if (SelectedPhotoKey is not null)
                DeletePhoto(SelectedPhotoKey);
        };
        AutomationProperties.SetName(deleteButton, "Foto löschen");
        photoList.SelectionChanged += (_, _) => ShowSelectedPhoto();
        photoList.MouseDoubleClick += (_, _) => UseSelectedPhoto();
        ScrollViewer.SetCanContentScroll(photoList, true);
        VirtualizingPanel.SetIsVirtualizing(photoList, true);
        VirtualizingPanel.SetVirtualizationMode(photoList, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(photoList, ScrollUnit.Pixel);
        searchBox.TextChanged += (_, _) =>
        {
            UpdateSearchBoxAffordances();
            searchTimer.Stop();
            searchTimer.Start();
        };
        searchBox.Template = CreateSearchTextBoxTemplate();
        searchBox.GotKeyboardFocus += (_, _) => UpdateSearchBoxAffordances();
        searchBox.LostKeyboardFocus += (_, _) => UpdateSearchBoxAffordances();
        clearSearchButton.Click += (_, _) =>
        {
            searchBox.Clear();
            searchBox.Focus();
            searchBox.CaretIndex = 0;
        };
        AutomationProperties.SetName(clearSearchButton, "Suche löschen");
        searchTimer.Tick += (_, _) =>
        {
            searchTimer.Stop();
            RefreshGallery();
        };
        renameButton.Click += (_, _) => RenameSelectedPhoto();
        previewName.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Enter)
                return;
            args.Handled = true;
            RenameSelectedPhoto();
        };
        useButton.IsDefault = false;

        var libraryHeading = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var sortLabel = new TextBlock
        {
            Text = "Sortieren:",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134)),
            Margin = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(sortPicker, Dock.Right);
        libraryHeading.Children.Add(sortPicker);
        libraryHeading.Children.Add(sortLabel);

        var searchGrid = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        var searchIcon = new SharpVectors.Converters.SvgViewbox
        {
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Source = new Uri("/assets/icons/find-icon.svg", UriKind.Relative)
        };
        Grid.SetColumn(searchIcon, 0);
        var searchInput = new Grid();
        searchInput.Children.Add(searchBox);
        searchInput.Children.Add(searchPlaceholder);
        Grid.SetColumn(searchInput, 2);
        Grid.SetColumn(clearSearchButton, 3);
        searchGrid.Children.Add(searchIcon);
        searchGrid.Children.Add(searchInput);
        searchGrid.Children.Add(clearSearchButton);
        searchBoxContainer.Child = searchGrid;
        searchBoxContainer.PreviewMouseLeftButtonDown += (_, args) =>
        {
            var source = args.OriginalSource as DependencyObject;
            var clickedClearButton = source is not null &&
                (ReferenceEquals(source, clearSearchButton) || clearSearchButton.IsAncestorOf(source));
            var clickedTextBox = source is not null &&
                (ReferenceEquals(source, searchBox) || searchBox.IsAncestorOf(source));
            if (!clickedClearButton && !clickedTextBox)
            {
                searchBox.Focus();
                args.Handled = true;
            }
        };

        var libraryPanel = new DockPanel();
        DockPanel.SetDock(libraryHeading, Dock.Top);
        libraryPanel.Children.Add(libraryHeading);
        DockPanel.SetDock(searchBoxContainer, Dock.Top);
        libraryPanel.Children.Add(searchBoxContainer);
        libraryPanel.Children.Add(photoList);

        var previewSurface = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(247, 249, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 238)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = new Grid
            {
                Children =
                {
                    preview,
                    previewEmpty
                }
            }
        };
        var previewDetails = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        previewDetails.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewDetails.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        previewDetails.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        previewDetails.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var metadata = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        metadata.Children.Add(previewName);
        metadata.Children.Add(previewKey);
        metadata.Children.Add(previewAddedAt);
        Grid.SetColumn(metadata, 0);
        Grid.SetColumn(renameButton, 1);
        Grid.SetColumn(deleteButton, 3);
        previewDetails.Children.Add(metadata);
        previewDetails.Children.Add(renameButton);
        previewDetails.Children.Add(deleteButton);

        var detailPanel = new DockPanel();
        DockPanel.SetDock(previewDetails, Dock.Bottom);
        detailPanel.Children.Add(previewDetails);
        detailPanel.Children.Add(previewSurface);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(libraryPanel, 0);
        Grid.SetColumn(detailPanel, 2);
        content.Children.Add(libraryPanel);
        content.Children.Add(detailPanel);
        layout.Children.Add(content);

        Content = layout;
        PreviewMouseDown += (_, args) =>
        {
            var source = args.OriginalSource as DependencyObject;
            var clickedInsideSearch = source is not null &&
                (ReferenceEquals(source, searchBoxContainer) || searchBoxContainer.IsAncestorOf(source));
            if (!clickedInsideSearch && searchBox.IsKeyboardFocusWithin)
                Keyboard.ClearFocus();
        };
        Loaded += (_, _) =>
        {
            photoListScrollViewer = FindVisualChild<ScrollViewer>(photoList);
            UpdateSearchBoxAffordances();
            RefreshGallery();
        };
        Closed += (_, _) =>
        {
            searchTimer.Stop();
            previewLoadCancellation?.Cancel();
            thumbnailLoadCancellation.Cancel();
        };
    }

    private void AddPhotos_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Fotos zur Mediathek hinzufügen",
            Filter = "Bilddateien|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.svg",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            foreach (var file in dialog.FileNames)
                SelectedPhotoKey = photoLibrary.AddPhoto(file, catalog);
            CommitChanges();
            RefreshGallery();
            SelectPhoto(SelectedPhotoKey);
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
        var selectedKey = SelectedPhotoKey;
        photoList.Items.Clear();
        var photos = catalog.Library
            .Select(entry => new
            {
                entry.Key,
                entry.Value,
                Path = photoLibrary.GetPhotoPath(catalog, entry.Key),
                Name = catalog.PhotoNames.TryGetValue(entry.Key, out var name) ? name : entry.Value,
                AddedAt = catalog.PhotoAddedAt.TryGetValue(entry.Key, out var addedAt)
                    ? addedAt
                    : DateTimeOffset.MinValue
            })
            .Where(photo => photo.Path is not null);
        var searchText = searchBox.Text.Trim();
        if (searchText.Length > 0)
            photos = photos.Where(photo =>
                photo.Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                photo.Key.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        photos = sortPicker.SelectedIndex switch
        {
            1 => photos.OrderBy(photo => photo.AddedAt),
            2 => photos.OrderBy(photo => photo.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => photos.OrderByDescending(photo => photo.AddedAt)
        };

        var photoEntries = photos.ToList();
        var metadataChanged = false;
        foreach (var photo in photoEntries)
        {
            if (!catalog.PhotoNames.ContainsKey(photo.Key))
            {
                catalog.PhotoNames[photo.Key] = Path.GetFileName(photo.Value);
                metadataChanged = true;
            }

            if (!catalog.PhotoAddedAt.ContainsKey(photo.Key))
            {
                catalog.PhotoAddedAt[photo.Key] = new DateTimeOffset(File.GetCreationTimeUtc(photo.Path!));
                metadataChanged = true;
            }
        }
        if (metadataChanged)
            CommitChanges();

        ListBoxItem? selectedItem = null;
        var added = 0;
        foreach (var photo in photoEntries)
        {
            thumbnailCache.TryGetValue(photo.Path!, out var image);
            var thumbnailImage = new Image
            {
                Source = image,
                Stretch = Stretch.Uniform,
                Tag = (photo.Key, photo.Path!)
            };
            thumbnailImage.Loaded += ThumbnailImage_Loaded;
            var row = new Grid { Margin = new Thickness(2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var thumbnail = new Border
            {
                Width = 50,
                Height = 50,
                Background = Brushes.White,
                CornerRadius = new CornerRadius(4),
                Child = thumbnailImage
            };
            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(new TextBlock
            {
                Text = photo.Name,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = photo.Name
            });
            details.Children.Add(new TextBlock
            {
                Text = catalog.PhotoAddedAt[photo.Key].ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134))
            });
            Grid.SetColumn(details, 1);
            row.Children.Add(thumbnail);
            row.Children.Add(details);

            var item = new ListBoxItem
            {
                Content = row,
                Tag = photo.Key,
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 0, 4)
            };
            photoList.Items.Add(item);
            if (photo.Key == selectedKey)
                selectedItem = item;
            added++;
        }

        if (selectedItem is not null)
            photoList.SelectedItem = selectedItem;
        else
            ShowSelectedPhoto();

        if (added == 0 && searchText.Length > 0)
            status.Text = $"Keine Fotos für „{searchText}“ gefunden.";
        else if (added == 0)
            status.Text = "Die Mediathek ist leer. Fügen Sie Fotos hinzu oder importieren Sie einen JSON-Katalog mit Fotos.";
        else
            status.Text = $"{added} Foto(s) in der lokalen Mediathek";
    }

    private void UpdateSearchBoxAffordances()
    {
        var hasText = !string.IsNullOrEmpty(searchBox.Text);
        searchPlaceholder.Visibility = !hasText && !searchBox.IsKeyboardFocusWithin
            ? Visibility.Visible
            : Visibility.Collapsed;
        clearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ControlTemplate CreateSearchTextBoxTemplate()
    {
        var contentHost = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        contentHost.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding(nameof(Control.Padding))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        contentHost.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        contentHost.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        contentHost.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        contentHost.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var border = new FrameworkElementFactory(typeof(Border));
        border.AppendChild(contentHost);
        return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
    }

    private static BitmapImage LoadImage(string path, int maxDimension)
    {
        var imageBytes = File.ReadAllBytes(path);
        using var dimensionsStream = new MemoryStream(imageBytes, writable: false);
        var frame = BitmapFrame.Create(
            dimensionsStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var scale = Math.Min(1d, (double)maxDimension / Math.Max(frame.PixelWidth, frame.PixelHeight));
        var decodePixelWidth = Math.Max(1, (int)Math.Round(frame.PixelWidth * scale));
        var decodePixelHeight = Math.Max(1, (int)Math.Round(frame.PixelHeight * scale));

        var image = new BitmapImage();
        using (var imageStream = new MemoryStream(imageBytes, writable: false))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodePixelWidth;
            image.DecodePixelHeight = decodePixelHeight;
            image.StreamSource = imageStream;
            image.EndInit();
        }
        image.Freeze();
        return image;
    }

    private void ThumbnailImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image { Tag: ValueTuple<string, string> photo } thumbnailImage)
            return;

        if (thumbnailCache.TryGetValue(photo.Item2, out var cachedImage))
        {
            thumbnailImage.Source = cachedImage;
            return;
        }

        _ = LoadThumbnailAsync(photo.Item1, photo.Item2, thumbnailImage, thumbnailLoadCancellation.Token);
    }

    private async Task LoadThumbnailAsync(
        string key,
        string path,
        Image thumbnailImage,
        CancellationToken cancellationToken)
    {
        if (!thumbnailLoadsInProgress.Add(path))
            return;

        try
        {
            await thumbnailLoadGate.WaitAsync(cancellationToken);
            try
            {
                var image = await Task.Run(() => LoadImage(path, 64), cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    return;

                thumbnailCache[path] = image;
                if (thumbnailImage.IsLoaded)
                    thumbnailImage.Source = image;
            }
            finally
            {
                thumbnailLoadGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or FormatException or OutOfMemoryException)
        {
            if (!cancellationToken.IsCancellationRequested)
                AppDiagnostics.Exception("photo.thumbnail_load", ex);
        }
        finally
        {
            thumbnailLoadsInProgress.Remove(path);
        }
    }

    private void SelectPhoto(string? key)
    {
        if (key is null)
            return;
        var item = photoList.Items.OfType<ListBoxItem>().FirstOrDefault(candidate => Equals(candidate.Tag, key));
        if (item is not null)
            photoList.SelectedItem = item;
    }

    private void ShowSelectedPhoto()
    {
        previewLoadCancellation?.Cancel();
        if (photoList.SelectedItem is not ListBoxItem item || item.Tag is not string key)
        {
            SelectedPhotoKey = null;
            preview.Source = null;
            preview.Visibility = Visibility.Collapsed;
            previewEmpty.Visibility = Visibility.Visible;
            previewName.Text = "";
            previewName.IsEnabled = false;
            previewKey.Text = "";
            previewAddedAt.Text = "";
            useButton.IsEnabled = false;
            deleteButton.IsEnabled = false;
            renameButton.IsEnabled = false;
            return;
        }

        var path = photoLibrary.GetPhotoPath(catalog, key);
        if (path is null)
            return;

        SelectedPhotoKey = key;
        var cancellation = new CancellationTokenSource();
        previewLoadCancellation = cancellation;
        preview.Source = null;
        preview.Visibility = Visibility.Visible;
        previewEmpty.Visibility = Visibility.Collapsed;
        previewName.Text = catalog.PhotoNames.TryGetValue(key, out var name) ? name : Path.GetFileName(path);
        previewName.IsEnabled = true;
        previewKey.Text = key;
        previewAddedAt.Text = catalog.PhotoAddedAt.TryGetValue(key, out var addedAt)
            ? $"Hinzugefügt am {addedAt.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "";
        useButton.IsEnabled = true;
        deleteButton.IsEnabled = true;
        renameButton.IsEnabled = true;
        _ = LoadPreviewAsync(path, cancellation);
    }

    private async Task LoadPreviewAsync(string path, CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            await Task.Delay(125, cancellationToken);
            await previewLoadGate.WaitAsync(cancellationToken);
            BitmapImage image;
            try
            {
                image = await Task.Run(() => LoadImage(path, 1000), cancellationToken);
            }
            finally
            {
                previewLoadGate.Release();
            }

            if (cancellationToken.IsCancellationRequested)
                return;
            preview.Source = image;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or FormatException or OutOfMemoryException)
        {
            if (!cancellationToken.IsCancellationRequested)
                status.Text = $"Foto-Vorschau konnte nicht geladen werden: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(previewLoadCancellation, cancellation))
                previewLoadCancellation = null;
            cancellation.Dispose();
        }
    }

    private void RenameSelectedPhoto()
    {
        if (SelectedPhotoKey is not { } key)
            return;

        var newName = previewName.Text.Trim();
        if (newName.Length == 0)
        {
            status.Text = "Der Fotoname darf nicht leer sein.";
            return;
        }

        catalog.PhotoNames ??= [];
        catalog.PhotoNames[key] = newName;
        CommitChanges();
        RefreshGallery();
        SelectPhoto(key);
        status.Text = "Foto wurde umbenannt und gespeichert.";
    }

    private void DeletePhoto(string key)
    {
        var displayName = catalog.PhotoNames.TryGetValue(key, out var name) ? name : key;
        var references = CatalogData.GetPhotoReferences(catalog, key);
        var itemReferences = references.Where(reference => reference.Category != "Logo").ToList();
        var logoReferences = references.Where(reference => reference.Category == "Logo").ToList();
        var message = references.Count == 0
            ? $"Das Foto „{displayName}“ wird dauerhaft aus der lokalen Mediathek gelöscht."
            : $"Das Foto wird von {itemReferences.Count} Artikel(n) verwendet und ist in {logoReferences.Count} Katalog(en) als Firmenlogo hinterlegt. " +
              "Beim Löschen werden diese Foto- und Logo-Verknüpfungen entfernt:\n\n" +
              string.Join("\n", references.Take(8).Select(reference =>
                  reference.Category == "Logo"
                      ? $"Kataloglogo – {PhotoReferenceTitle(reference)}"
                      : $"{reference.Category} – {PhotoReferenceTitle(reference)} (Position {reference.Position})")) +
              (references.Count > 8 ? $"\n… und {references.Count - 8} weitere" : "");
        if (!ConfirmationPromptWindow.Show(this, "Foto löschen", message, "Löschen"))
            return;

        try
        {
            var path = photoLibrary.GetPhotoPath(catalog, key);
            var removedReferences = photoLibrary.RemovePhoto(catalog, key);
            if (path is not null)
                thumbnailCache.Remove(path);
            if (SelectedPhotoKey == key)
                SelectedPhotoKey = null;
            CommitChanges();
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

    private void CommitChanges()
    {
        HasChanges = true;
        saveChanges();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T result)
                return result;
            if (FindVisualChild<T>(child) is { } descendant)
                return descendant;
        }

        return null;
    }

    private void UseSelectedPhoto()
    {
        if (SelectedPhotoKey is not null)
            DialogResult = true;
    }
}
