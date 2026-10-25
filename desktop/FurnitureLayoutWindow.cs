using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FuniturenEditor.Desktop;

public sealed class FurnitureLayoutWindow : Window
{
    private readonly CatalogData catalog;
    private readonly CatalogItem sample;
    private readonly PhotoLibrary photoLibrary;
    private readonly FurniturePrintLayout draft;
    private readonly ComboBox elementSelector = new();
    private readonly ComboBox orientationSelector = new();
    private readonly ComboBox kindSelector = new();
    private readonly ComboBox sourceSelector = new();
    private readonly TextBox horizontalGapBox = new();
    private readonly TextBox verticalGapBox = new();
    private readonly TextBox columnsBox = new();
    private readonly TextBox tileCountBox = new();
    private readonly ComboBox qrSourceBox = new();
    private readonly ComboBox qrFallbackBox = new();
    private readonly TextBox qrPrefixBox = new();
    private readonly TextBox qrSuffixBox = new();
    private readonly CheckBox qrEnabledCheckBox = new() { Content = "QR-/Barcode-Erzeugung aktiv" };
    private readonly CheckBox qrFallbackCheckBox = new() { Content = "Fallback verwenden" };
    private readonly TextBox cellWidthBox = new();
    private readonly TextBox cellHeightBox = new();
    private readonly TextBox xBox = new();
    private readonly TextBox yBox = new();
    private readonly TextBox widthBox = new();
    private readonly TextBox heightBox = new();
    private readonly TextBox fontSizeBox = new();
    private readonly TextBox rotationBox = new();
    private readonly TextBox missingBecoTextBox = new();
    private readonly CheckBox boldCheckBox = new() { Content = "Fett darstellen" };
    private readonly CheckBox autoShrinkTextCheckBox = new()
    {
        Content = "Schrift automatisch verkleinern, damit die Beschreibung vollständig passt",
        Visibility = Visibility.Collapsed
    };
    private readonly StackPanel missingBecoTextPanel = new();
    private readonly Viewbox cellPreview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(24) };
    private readonly List<Thumb> resizeGrips = [];
    private readonly List<PrintElementLayout> overlapCycle = [];
    private Canvas? previewCanvas;
    private Rectangle? selectionOutline;
    private Line? verticalSnapGuide;
    private Line? horizontalSnapGuide;
    private PrintElementLayout? currentElement;
    private bool isUpdating;
    private bool isDragging;
    private Point dragStart;
    private Point lastCyclePoint;
    private double dragStartX;
    private double dragStartY;
    private TextBox? numericDragBox;
    private Point numericDragStartPoint;
    private double numericDragStartValue;
    private double numericDragStep;
    private bool isNumericDragging;
    private int overlapCycleIndex;
    private const double SnapToleranceCm = 0.12;

    public FurniturePrintLayout Result { get; }

    public FurnitureLayoutWindow(
        CatalogData catalog,
        CatalogItem sample,
        PhotoLibrary photoLibrary,
        FurniturePrintLayout currentLayout)
    {
        this.catalog = catalog;
        this.sample = sample;
        this.photoLibrary = photoLibrary;
        draft = currentLayout.Copy();
        Result = draft;

        Title = "Etikettenvorlage";
        Width = 920;
        Height = 650;
        MinWidth = 760;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(242, 245, 248));

        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Etikettenvorlage",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 43, 66))
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Die Vorlage gilt für alle Etiketten. Positionen und Größen sind in cm angegeben; Breite oder Höhe 0 blendet ein Element aus.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true };
        var reset = new Button { Content = "Standard wiederherstellen" };
        var save = new Button { Content = "Vorlage übernehmen", IsDefault = true, Margin = new Thickness(0) };
        cancel.Click += (_, _) => DialogResult = false;
        reset.Click += (_, _) => ResetLayout();
        save.Click += (_, _) =>
        {
            SaveSelectedElement();
            draft.Normalize();
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(reset);
        buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(columns);

        var form = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        AddSectionHeading(form, "Zellengröße");
        var cellSize = new Grid { Margin = new Thickness(0, 4, 0, 12) };
        cellSize.ColumnDefinitions.Add(new ColumnDefinition());
        cellSize.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        cellSize.ColumnDefinitions.Add(new ColumnDefinition());
        cellSize.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cellSize.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddNumericField(cellSize, "Breite (cm)", cellWidthBox, 0.1, 0, 0);
        AddNumericField(cellSize, "Höhe (cm)", cellHeightBox, 0.1, 2, 0);
        form.Children.Add(cellSize);

        AddSectionHeading(form, "Druckraster");
        orientationSelector.ItemsSource = new[] { "Landscape", "Portrait" };
        orientationSelector.Margin = new Thickness(0, 4, 0, 8);
        form.Children.Add(orientationSelector);
        var gridSettings = new Grid();
        gridSettings.ColumnDefinitions.Add(new ColumnDefinition());
        gridSettings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        gridSettings.ColumnDefinitions.Add(new ColumnDefinition());
        gridSettings.RowDefinitions.Add(new RowDefinition());
        gridSettings.RowDefinitions.Add(new RowDefinition());
        AddNumericField(gridSettings, "Horizontaler Abstand (cm)", horizontalGapBox, 0.1, 0, 0);
        AddNumericField(gridSettings, "Vertikaler Abstand (cm)", verticalGapBox, 0.1, 2, 0);
        AddNumericField(gridSettings, "Spalten", columnsBox, 1, 0, 1);
        AddNumericField(gridSettings, "Etiketten je Kategorie", tileCountBox, 1, 2, 1);
        form.Children.Add(gridSettings);

        AddSectionHeading(form, "Element bearbeiten");
        var elementButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        var addElement = new Button { Content = "Feld hinzufügen", Margin = new Thickness(0, 0, 6, 0) };
        var removeElement = new Button { Content = "Feld entfernen" };
        addElement.Click += (_, _) => AddElement();
        removeElement.Click += (_, _) => RemoveElement();
        elementButtons.Children.Add(addElement);
        elementButtons.Children.Add(removeElement);
        form.Children.Add(elementButtons);
        elementSelector.Margin = new Thickness(0, 0, 0, 8);
        elementSelector.DisplayMemberPath = nameof(LayoutChoice.Name);
        elementSelector.SelectedValuePath = nameof(LayoutChoice.Key);
        form.Children.Add(elementSelector);
        kindSelector.ItemsSource = new[] { "Text", "Photo", "Qr", "Barcode", "Price" };
        sourceSelector.ItemsSource = new[] { "Title", "Beco", "Ref", "Price", "SalesPrice", "Ek", "RecommendedPrice" };
        form.Children.Add(kindSelector);
        form.Children.Add(sourceSelector);

        var positionGrid = new Grid();
        positionGrid.ColumnDefinitions.Add(new ColumnDefinition());
        positionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        positionGrid.ColumnDefinitions.Add(new ColumnDefinition());
        positionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        positionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        positionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        positionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddNumericField(positionGrid, "Links (X, cm)", xBox, 0.1, 0, 0);
        AddNumericField(positionGrid, "Oben (Y, cm)", yBox, 0.1, 2, 0);
        AddNumericField(positionGrid, "Breite (cm)", widthBox, 0.1, 0, 1);
        AddNumericField(positionGrid, "Höhe (cm)", heightBox, 0.1, 2, 1);
        AddNumericField(positionGrid, "Schriftgröße (pt)", fontSizeBox, 0.25, 0, 2);
        AddNumericField(positionGrid, "Drehung (°)", rotationBox, 1, 2, 2);
        form.Children.Add(positionGrid);
        boldCheckBox.Margin = new Thickness(0, 8, 0, 0);
        form.Children.Add(boldCheckBox);
        autoShrinkTextCheckBox.Margin = new Thickness(0, 8, 0, 0);
        form.Children.Add(autoShrinkTextCheckBox);
        missingBecoTextPanel.Margin = new Thickness(0, 8, 0, 0);
        missingBecoTextPanel.Children.Add(new TextBlock
        {
            Text = "Text ohne BECO-Nummer",
            Foreground = new SolidColorBrush(Color.FromRgb(67, 87, 106))
        });
        missingBecoTextBox.MinWidth = 100;
        missingBecoTextPanel.Children.Add(missingBecoTextBox);
        form.Children.Add(missingBecoTextPanel);
        AddSectionHeading(form, "QR-/Barcode-Einstellungen");
        form.Children.Add(qrEnabledCheckBox);
        form.Children.Add(new TextBlock { Text = "Primärfeld", Margin = new Thickness(0, 4, 0, 0) });
        qrSourceBox.ItemsSource = new[] { "Title", "Beco", "Ref", "Price", "SalesPrice", "Ek", "RecommendedPrice" };
        form.Children.Add(qrSourceBox);
        form.Children.Add(new TextBlock { Text = "Fallbackfeld" });
        qrFallbackBox.ItemsSource = new[] { "Title", "Beco", "Ref", "Price", "SalesPrice", "Ek", "RecommendedPrice" };
        form.Children.Add(qrFallbackBox);
        form.Children.Add(qrFallbackCheckBox);
        form.Children.Add(new TextBlock { Text = "Präfix" });
        form.Children.Add(qrPrefixBox);
        form.Children.Add(new TextBlock { Text = "Suffix" });
        form.Children.Add(qrSuffixBox);
        var note = new TextBlock
        {
            Text = "Element links auswählen und in der Vorschau ziehen. Rechtsklick schaltet überlappende Elemente durch. Die blaue Kontur markiert die Auswahl; die Griffe verändern die Größe. Zahleneingaben lassen sich über die Pfeile oder durch horizontales Ziehen bei gedrückter Maustaste ändern.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134)),
            Margin = new Thickness(0, 16, 0, 0)
        };
        form.Children.Add(note);

        var formScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = form
        };
        columns.Children.Add(formScroll);

        var previewPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(232, 238, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 224, 232)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = cellPreview
        };
        Grid.SetColumn(previewPanel, 1);
        columns.Children.Add(previewPanel);
        DockPanel.SetDock(columns, Dock.Top);
        Content = root;

        cellWidthBox.TextChanged += CellSize_Changed;
        cellHeightBox.TextChanged += CellSize_Changed;
        orientationSelector.SelectionChanged += LayoutSettings_Changed;
        horizontalGapBox.TextChanged += LayoutSettings_Changed;
        verticalGapBox.TextChanged += LayoutSettings_Changed;
        columnsBox.TextChanged += LayoutSettings_Changed;
        tileCountBox.TextChanged += LayoutSettings_Changed;
        elementSelector.SelectionChanged += ElementSelector_SelectionChanged;
        kindSelector.SelectionChanged += ElementSettings_Changed;
        sourceSelector.SelectionChanged += ElementSettings_Changed;
        xBox.TextChanged += Element_Changed;
        yBox.TextChanged += Element_Changed;
        widthBox.TextChanged += Element_Changed;
        heightBox.TextChanged += Element_Changed;
        fontSizeBox.TextChanged += Element_Changed;
        rotationBox.TextChanged += Element_Changed;
        boldCheckBox.Checked += Element_Changed;
        boldCheckBox.Unchecked += Element_Changed;
        autoShrinkTextCheckBox.Checked += Element_Changed;
        autoShrinkTextCheckBox.Unchecked += Element_Changed;
        missingBecoTextBox.TextChanged += MissingBecoText_Changed;
        qrEnabledCheckBox.Checked += QrSettings_Changed;
        qrEnabledCheckBox.Unchecked += QrSettings_Changed;
        qrFallbackCheckBox.Checked += QrSettings_Changed;
        qrFallbackCheckBox.Unchecked += QrSettings_Changed;
        qrSourceBox.SelectionChanged += QrSettings_Changed;
        qrFallbackBox.SelectionChanged += QrSettings_Changed;
        qrPrefixBox.TextChanged += QrSettings_Changed;
        qrSuffixBox.TextChanged += QrSettings_Changed;
        foreach (var numericField in new[]
        {
            cellWidthBox, cellHeightBox, xBox, yBox, widthBox, heightBox, fontSizeBox, rotationBox
        })
            numericField.LostKeyboardFocus += NumericField_LostKeyboardFocus;
        CreateResizeGrips();
        Loaded += (_, _) =>
        {
            LoadFields();
            RefreshPreview();
        };
    }

    private void CellSize_Changed(object sender, TextChangedEventArgs e)
    {
        if (isUpdating || !SaveCellSize())
            return;
        RefreshPreview();
    }

    private void LayoutSettings_Changed(object sender, EventArgs e)
    {
        if (isUpdating)
            return;

        if (orientationSelector.SelectedItem is string orientation)
            draft.Orientation = orientation;
        if (TryRead(horizontalGapBox, out var horizontalGap))
            draft.HorizontalGapCm = horizontalGap;
        if (TryRead(verticalGapBox, out var verticalGap))
            draft.VerticalGapCm = verticalGap;
        if (int.TryParse(columnsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var columns))
            draft.Columns = columns;
        if (int.TryParse(tileCountBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var tileCount))
            draft.TileCount = tileCount;
        draft.Normalize();
        RefreshPreview();
    }

    private void ElementSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdating || currentElement is null)
            return;
        currentElement.Kind = kindSelector.SelectedItem as string ?? currentElement.Kind;
        currentElement.Source = sourceSelector.SelectedItem as string ?? currentElement.Source;
        RefreshPreview();
    }

    private void QrSettings_Changed(object sender, EventArgs e)
    {
        if (isUpdating)
            return;
        draft.QrCode.Enabled = qrEnabledCheckBox.IsChecked == true;
        draft.QrCode.UseFallback = qrFallbackCheckBox.IsChecked == true;
        draft.QrCode.SourceField = qrSourceBox.SelectedItem as string ?? "Ref";
        draft.QrCode.FallbackField = qrFallbackBox.SelectedItem as string ?? "Beco";
        draft.QrCode.Prefix = qrPrefixBox.Text;
        draft.QrCode.Suffix = qrSuffixBox.Text;
        draft.QrCode.Normalize();
        RefreshPreview();
    }

    private void AddElement()
    {
        SaveSelectedElement();
        var element = new PrintElementLayout
        {
            Name = "Neues Feld",
            Kind = "Text",
            Source = "Title",
            X = 0.15,
            Y = Math.Max(0, draft.CellHeightCm - 0.4),
            Width = Math.Max(0.5, draft.CellWidthCm - 0.3),
            Height = 0.3,
            FontSize = 6
        };
        draft.Fields.Add(element);
        RefreshElementSelector(element);
        RefreshPreview();
    }

    private void RemoveElement()
    {
        if (currentElement is null || draft.Fields.Count <= 1)
            return;
        draft.Fields.Remove(currentElement);
        currentElement = draft.Fields.FirstOrDefault();
        RefreshElementSelector(currentElement);
        RefreshPreview();
    }

    private void RefreshElementSelector(PrintElementLayout? selected = null)
    {
        isUpdating = true;
        elementSelector.ItemsSource = draft.Fields
            .Select(field => new LayoutChoice(field.Name, field.Id))
            .ToList();
        elementSelector.SelectedValue = selected?.Id;
        if (elementSelector.SelectedIndex < 0 && elementSelector.Items.Count > 0)
            elementSelector.SelectedIndex = 0;
        isUpdating = false;
        LoadElementFields();
    }

    private bool SaveCellSize()
    {
        if (!TryRead(cellWidthBox, out var cellWidth) ||
            !TryRead(cellHeightBox, out var cellHeight))
            return false;

        draft.CellWidthCm = Math.Clamp(cellWidth, 2, 5);
        draft.CellHeightCm = Math.Clamp(cellHeight, 2, 5);
        draft.Normalize();
        return true;
    }

    private void ElementSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdating)
            return;
        SaveSelectedElement();
        currentElement = SelectedElement();
        LoadElementFields();
        RefreshPreview();
    }

    private void Element_Changed(object sender, RoutedEventArgs e)
    {
        if (isUpdating)
            return;
        SaveSelectedElement();
        RefreshPreview();
    }

    private void Element_Changed(object sender, TextChangedEventArgs e)
    {
        if (isUpdating)
            return;
        SaveSelectedElement();
        RefreshPreview();
    }

    private void MissingBecoText_Changed(object sender, TextChangedEventArgs e)
    {
        if (isUpdating)
            return;

        draft.MissingBecoText = missingBecoTextBox.Text;
        RefreshPreview();
    }

    private void NumericField_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (isUpdating || sender is not TextBox box || IsIncompleteNumber(box.Text))
            return;

        if (ReferenceEquals(box, cellWidthBox) || ReferenceEquals(box, cellHeightBox))
        {
            if (SaveCellSize())
            {
                LoadFields();
                RefreshPreview();
            }
            return;
        }

        SaveSelectedElement();
        LoadElementFields();
        RefreshPreview();
    }

    private void NumericField_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box || !TryRead(box, out var value))
            return;

        numericDragBox = box;
        numericDragStartPoint = e.GetPosition(box);
        numericDragStartValue = value;
        numericDragStep = (double)box.Tag;
        isNumericDragging = false;
        box.CaptureMouse();
    }

    private void NumericField_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not TextBox box ||
            !ReferenceEquals(box, numericDragBox) ||
            e.LeftButton != MouseButtonState.Pressed)
            return;

        var horizontalDistance = e.GetPosition(box).X - numericDragStartPoint.X;
        var steps = (int)Math.Truncate(horizontalDistance / 8);
        if (steps == 0)
            return;

        isNumericDragging = true;
        box.Text = Format(numericDragStartValue + steps * numericDragStep);
        e.Handled = true;
    }

    private void NumericField_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box || !ReferenceEquals(box, numericDragBox))
            return;

        var wasDragging = isNumericDragging;
        box.ReleaseMouseCapture();
        FinishNumericDrag(box);
        if (wasDragging)
            e.Handled = true;
    }

    private void NumericField_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is TextBox box && ReferenceEquals(box, numericDragBox))
            FinishNumericDrag(box);
    }

    private void FinishNumericDrag(TextBox box)
    {
        var wasDragging = isNumericDragging;
        numericDragBox = null;
        isNumericDragging = false;
        if (!wasDragging)
            return;

        if (ReferenceEquals(box, cellWidthBox) || ReferenceEquals(box, cellHeightBox))
        {
            if (SaveCellSize())
                LoadFields();
        }
        else
        {
            SaveSelectedElement();
            LoadElementFields();
        }

        RefreshPreview();
    }

    private void SaveSelectedElement()
    {
        var element = currentElement;
        if (element is null ||
            !TryRead(xBox, out var x) ||
            !TryRead(yBox, out var y) ||
            !TryRead(widthBox, out var width) ||
            !TryRead(heightBox, out var height) ||
            !TryRead(fontSizeBox, out var fontSize) ||
            !TryRead(rotationBox, out var rotation))
            return;

        element.X = Math.Clamp(x, 0, draft.CellWidthCm);
        element.Y = Math.Clamp(y, 0, draft.CellHeightCm);
        element.Width = Math.Clamp(width, 0, draft.CellWidthCm - element.X);
        element.Height = Math.Clamp(height, 0, draft.CellHeightCm - element.Y);
        element.FontSize = Math.Clamp(fontSize, 4, 24);
        element.RotationDegrees = Math.Clamp(rotation, -180, 180);
        element.Bold = boldCheckBox.IsChecked == true;
        element.AutoShrinkText = autoShrinkTextCheckBox.IsChecked == true;
        element.Name = element.Kind switch
        {
            "Photo" => "Foto",
            "Qr" => "QR-Code",
            "Barcode" => "Barcode",
            "Price" => "Preis",
            _ => element.Source switch
            {
                "Title" => "Beschreibung",
                "Beco" => "BECO",
                "Ref" => "Referenznummer",
                "Ek" => "EK",
                _ => "Textfeld"
            }
        };
    }

    private void LoadFields()
    {
        isUpdating = true;
        cellWidthBox.Text = Format(draft.CellWidthCm);
        cellHeightBox.Text = Format(draft.CellHeightCm);
        orientationSelector.SelectedItem = draft.Orientation;
        horizontalGapBox.Text = Format(draft.HorizontalGapCm);
        verticalGapBox.Text = Format(draft.VerticalGapCm);
        columnsBox.Text = draft.Columns.ToString(CultureInfo.CurrentCulture);
        tileCountBox.Text = draft.TileCount.ToString(CultureInfo.CurrentCulture);
        qrEnabledCheckBox.IsChecked = draft.QrCode.Enabled;
        qrFallbackCheckBox.IsChecked = draft.QrCode.UseFallback;
        qrSourceBox.SelectedItem = draft.QrCode.SourceField;
        qrFallbackBox.SelectedItem = draft.QrCode.FallbackField;
        qrPrefixBox.Text = draft.QrCode.Prefix;
        qrSuffixBox.Text = draft.QrCode.Suffix;
        RefreshElementSelector(currentElement);
        isUpdating = false;
        LoadElementFields();
    }

    private void LoadElementFields()
    {
        isUpdating = true;
        currentElement = SelectedElement();
        var item = currentElement;
        xBox.Text = item is null ? "" : Format(item.X);
        yBox.Text = item is null ? "" : Format(item.Y);
        widthBox.Text = item is null ? "" : Format(item.Width);
        heightBox.Text = item is null ? "" : Format(item.Height);
        fontSizeBox.Text = item is null ? "" : Format(item.FontSize);
        rotationBox.Text = item is null ? "" : Format(item.RotationDegrees);
        kindSelector.SelectedItem = item?.Kind;
        sourceSelector.SelectedItem = item?.Source;
        boldCheckBox.IsChecked = item?.Bold == true;
        autoShrinkTextCheckBox.Visibility = item?.Kind == "Text" && item.Source == "Title"
            ? Visibility.Visible
            : Visibility.Collapsed;
        autoShrinkTextCheckBox.IsChecked = item?.AutoShrinkText != false;
        missingBecoTextPanel.Visibility = item?.Source == "Beco"
            ? Visibility.Visible
            : Visibility.Collapsed;
        missingBecoTextBox.Text = draft.MissingBecoText;
        isUpdating = false;
    }

    private PrintElementLayout? SelectedElement()
    {
        var id = (elementSelector.SelectedItem as LayoutChoice)?.Key;
        return draft.Fields.FirstOrDefault(field => field.Id == id);
    }

    private void RefreshPreview()
    {
        draft.Normalize();
        if (previewCanvas is not null)
        {
            foreach (var grip in resizeGrips)
                previewCanvas.Children.Remove(grip);
        }

        var cell = PrintService.CreateFurnitureCellPreview(catalog, sample, photoLibrary, draft);
        previewCanvas = cell.Child as Canvas;
        if (previewCanvas is not null)
        {
            previewCanvas.MouseMove += PreviewCanvas_MouseMove;
            previewCanvas.MouseLeftButtonUp += PreviewCanvas_MouseLeftButtonUp;
            CreatePreviewOverlays();
            foreach (var element in previewCanvas.Children.OfType<FrameworkElement>())
            {
                if (element.Tag is not PrintElementLayout layout)
                    continue;
                element.Cursor = Cursors.SizeAll;
                element.PreviewMouseLeftButtonDown += PreviewElement_MouseLeftButtonDown;
                element.PreviewMouseRightButtonDown += PreviewElement_MouseRightButtonDown;
            }

            if (currentElement is not null)
            {
                UpdateSelectionOutline();
                AddResizeGrips();
            }
        }
        cellPreview.Child = cell;
    }

    private void PreviewElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement || currentElement is null || previewCanvas is null)
            return;
        isDragging = true;
        dragStart = e.GetPosition(previewCanvas);
        dragStartX = currentElement.X;
        dragStartY = currentElement.Y;
        previewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewElement_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (previewCanvas is null)
            return;

        var point = e.GetPosition(previewCanvas);
        var candidates = previewCanvas.Children
            .OfType<FrameworkElement>()
            .Select((element, index) => (element, index))
            .Where(item => item.element.Tag is PrintElementLayout layout &&
                IsPointInsideLayout(point, layout))
            .OrderByDescending(item => Panel.GetZIndex(item.element))
            .ThenByDescending(item => item.index)
            .Select(item => (PrintElementLayout)item.element.Tag!)
            .ToList();
        if (candidates.Count == 0)
            return;

        if (!IsSameCyclePoint(point) || !overlapCycle.SequenceEqual(candidates))
        {
            overlapCycle.Clear();
            overlapCycle.AddRange(candidates);
            overlapCycleIndex = 0;
        }
        else
        {
            overlapCycleIndex = (overlapCycleIndex + 1) % overlapCycle.Count;
        }

        lastCyclePoint = point;
        SelectElement(overlapCycle[overlapCycleIndex]);
        e.Handled = true;
    }

    private bool IsSameCyclePoint(Point point) =>
        overlapCycle.Count > 0 &&
        Math.Abs(point.X - lastCyclePoint.X) <= 4 &&
        Math.Abs(point.Y - lastCyclePoint.Y) <= 4;

    private static bool IsPointInsideLayout(Point point, PrintElementLayout layout)
    {
        var left = CmToDip(layout.X);
        var top = CmToDip(layout.Y);
        return point.X >= left &&
            point.X <= left + CmToDip(layout.Width) &&
            point.Y >= top &&
            point.Y <= top + CmToDip(layout.Height);
    }

    private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isDragging || currentElement is null || previewCanvas is null)
            return;
        var point = e.GetPosition(previewCanvas);
        var desiredX = Math.Clamp(
            dragStartX + DipToCm(point.X - dragStart.X),
            0,
            Math.Max(0, draft.CellWidthCm - currentElement.Width));
        var desiredY = Math.Clamp(
            dragStartY + DipToCm(point.Y - dragStart.Y),
            0,
            Math.Max(0, draft.CellHeightCm - currentElement.Height));
        var otherElements = previewCanvas.Children
            .OfType<FrameworkElement>()
            .Select(child => child.Tag)
            .OfType<PrintElementLayout>()
            .Where(element => !ReferenceEquals(element, currentElement))
            .ToArray();
        currentElement.X = SnapPosition(
            desiredX, currentElement.Width, draft.CellWidthCm, otherElements, horizontal: true, out var guideX);
        currentElement.Y = SnapPosition(
            desiredY, currentElement.Height, draft.CellHeightCm, otherElements, horizontal: false, out var guideY);
        SetSnapGuides(guideX, guideY);
        UpdateElementVisual();
        LoadElementFields();
    }

    private void PreviewCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isDragging || previewCanvas is null)
            return;
        isDragging = false;
        previewCanvas.ReleaseMouseCapture();
        SetSnapGuides(null, null);
        LoadElementFields();
        SaveSelectedElement();
        RefreshPreview();
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (currentElement is null || sender is not Thumb { Tag: ResizeEdges edges })
            return;

        var dx = DipToCm(e.HorizontalChange);
        var dy = DipToCm(e.VerticalChange);
        if (edges.HasFlag(ResizeEdges.Left))
        {
            var adjusted = Math.Clamp(dx, -currentElement.X, currentElement.Width - 0.1);
            currentElement.X += adjusted;
            currentElement.Width -= adjusted;
        }
        if (edges.HasFlag(ResizeEdges.Right))
            currentElement.Width = Math.Clamp(currentElement.Width + dx, 0.1, draft.CellWidthCm - currentElement.X);
        if (edges.HasFlag(ResizeEdges.Top))
        {
            var adjusted = Math.Clamp(dy, -currentElement.Y, currentElement.Height - 0.1);
            currentElement.Y += adjusted;
            currentElement.Height -= adjusted;
        }
        if (edges.HasFlag(ResizeEdges.Bottom))
            currentElement.Height = Math.Clamp(currentElement.Height + dy, 0.1, draft.CellHeightCm - currentElement.Y);

        UpdateElementVisual();
        LoadElementFields();
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        SaveSelectedElement();
        RefreshPreview();
    }

    private void SelectElement(PrintElementLayout selected)
    {
        currentElement = selected;
        if (elementSelector.ItemsSource is IEnumerable<LayoutChoice> choices)
        {
            var key = selected == draft.Photo ? "Photo" :
                selected == draft.Qr ? "Qr" :
                selected == draft.Title ? "Title" :
                selected == draft.Beco ? "Beco" :
                selected == draft.Ref ? "Ref" :
                selected == draft.Price ? "Price" : "Ek";
            isUpdating = true;
            elementSelector.SelectedItem = choices.First(choice => choice.Key == key);
            isUpdating = false;
        }
        LoadElementFields();
        UpdateSelectionOutline();
        UpdateResizeGripPositions();
    }

    private void CreatePreviewOverlays()
    {
        if (previewCanvas is null)
            return;

        selectionOutline = new Rectangle
        {
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromRgb(30, 111, 190)),
            StrokeThickness = 1.25,
            StrokeDashArray = [3, 2],
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        verticalSnapGuide = CreateSnapGuide();
        horizontalSnapGuide = CreateSnapGuide();
        Panel.SetZIndex(selectionOutline, 900);
        Panel.SetZIndex(verticalSnapGuide, 950);
        Panel.SetZIndex(horizontalSnapGuide, 950);
        previewCanvas.Children.Add(selectionOutline);
        previewCanvas.Children.Add(verticalSnapGuide);
        previewCanvas.Children.Add(horizontalSnapGuide);
    }

    private static Line CreateSnapGuide() => new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(220, 83, 50)),
        StrokeThickness = 1,
        StrokeDashArray = [3, 2],
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };

    private void UpdateSelectionOutline()
    {
        if (previewCanvas is null || selectionOutline is null || currentElement is null)
            return;

        selectionOutline.Width = CmToDip(currentElement.Width);
        selectionOutline.Height = CmToDip(currentElement.Height);
        Canvas.SetLeft(selectionOutline, CmToDip(currentElement.X));
        Canvas.SetTop(selectionOutline, CmToDip(currentElement.Y));
        selectionOutline.Visibility = Visibility.Visible;
    }

    private void SetSnapGuides(double? x, double? y)
    {
        if (previewCanvas is null || verticalSnapGuide is null || horizontalSnapGuide is null)
            return;

        verticalSnapGuide.Visibility = x.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (x.HasValue)
        {
            verticalSnapGuide.X1 = verticalSnapGuide.X2 = CmToDip(x.Value);
            verticalSnapGuide.Y1 = 0;
            verticalSnapGuide.Y2 = previewCanvas.Height;
        }

        horizontalSnapGuide.Visibility = y.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (y.HasValue)
        {
            horizontalSnapGuide.X1 = 0;
            horizontalSnapGuide.X2 = previewCanvas.Width;
            horizontalSnapGuide.Y1 = horizontalSnapGuide.Y2 = CmToDip(y.Value);
        }
    }

    private static double SnapPosition(
        double position,
        double elementSize,
        double cellSize,
        IReadOnlyCollection<PrintElementLayout> otherElements,
        bool horizontal,
        out double? guidePosition)
    {
        var maxPosition = Math.Max(0, cellSize - elementSize);
        var movingAnchors = new[] { 0d, elementSize / 2, elementSize };
        var targetAnchors = new List<double> { 0, cellSize / 2, cellSize };
        foreach (var element in otherElements)
        {
            var elementPosition = horizontal ? element.X : element.Y;
            var otherSize = horizontal ? element.Width : element.Height;
            targetAnchors.Add(elementPosition);
            targetAnchors.Add(elementPosition + otherSize / 2);
            targetAnchors.Add(elementPosition + otherSize);
        }

        var bestPosition = position;
        var bestGuide = double.NaN;
        var bestDistance = SnapToleranceCm;
        foreach (var movingAnchor in movingAnchors)
        {
            foreach (var targetAnchor in targetAnchors)
            {
                var snappedPosition = targetAnchor - movingAnchor;
                if (snappedPosition < 0 || snappedPosition > maxPosition)
                    continue;

                var distance = Math.Abs(snappedPosition - position);
                if (distance > bestDistance)
                    continue;

                bestDistance = distance;
                bestPosition = snappedPosition;
                bestGuide = targetAnchor;
            }
        }

        guidePosition = double.IsNaN(bestGuide) ? null : bestGuide;
        return bestPosition;
    }

    private void CreateResizeGrips()
    {
        var handles = new (ResizeEdges Edges, Cursor Cursor)[]
        {
            (ResizeEdges.Left | ResizeEdges.Top, Cursors.SizeNWSE),
            (ResizeEdges.Top, Cursors.SizeNS),
            (ResizeEdges.Right | ResizeEdges.Top, Cursors.SizeNESW),
            (ResizeEdges.Right, Cursors.SizeWE),
            (ResizeEdges.Right | ResizeEdges.Bottom, Cursors.SizeNWSE),
            (ResizeEdges.Bottom, Cursors.SizeNS),
            (ResizeEdges.Left | ResizeEdges.Bottom, Cursors.SizeNESW),
            (ResizeEdges.Left, Cursors.SizeWE)
        };

        foreach (var (edges, cursor) in handles)
        {
            var grip = new Thumb
            {
                Width = 4,
                Height = 4,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 111, 190)),
                BorderThickness = new Thickness(1),
                Cursor = cursor,
                Tag = edges,
                Visibility = Visibility.Collapsed
            };
            grip.DragDelta += ResizeGrip_DragDelta;
            grip.DragCompleted += ResizeGrip_DragCompleted;
            resizeGrips.Add(grip);
        }
    }

    private void AddResizeGrips()
    {
        if (previewCanvas is null || currentElement is null)
            return;
        foreach (var grip in resizeGrips)
        {
            grip.Visibility = Visibility.Visible;
            Panel.SetZIndex(grip, 1000);
            previewCanvas.Children.Add(grip);
        }
        UpdateResizeGripPositions();
    }

    private void UpdateResizeGripPositions()
    {
        if (previewCanvas is null || currentElement is null)
            return;

        var left = CmToDip(currentElement.X);
        var top = CmToDip(currentElement.Y);
        var right = CmToDip(currentElement.X + currentElement.Width);
        var bottom = CmToDip(currentElement.Y + currentElement.Height);
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        var positions = new (double X, double Y)[]
        {
            (left, top), (centerX, top), (right, top), (right, centerY),
            (right, bottom), (centerX, bottom), (left, bottom), (left, centerY)
        };

        for (var index = 0; index < resizeGrips.Count; index++)
        {
            Canvas.SetLeft(resizeGrips[index], positions[index].X - resizeGrips[index].Width / 2);
            Canvas.SetTop(resizeGrips[index], positions[index].Y - resizeGrips[index].Height / 2);
        }
    }

    private void UpdateElementVisual()
    {
        if (previewCanvas is null || currentElement is null)
            return;
        var visual = previewCanvas.Children.OfType<FrameworkElement>()
            .FirstOrDefault(child => ReferenceEquals(child.Tag, currentElement));
        if (visual is not null)
        {
            visual.Width = CmToDip(currentElement.Width);
            visual.Height = CmToDip(currentElement.Height);
            Canvas.SetLeft(visual, CmToDip(currentElement.X));
            Canvas.SetTop(visual, CmToDip(currentElement.Y));
        }
        UpdateSelectionOutline();
        UpdateResizeGripPositions();
    }

    private static double CmToDip(double centimeters) => centimeters * 96 / 2.54;

    private static double DipToCm(double dips) => dips * 2.54 / 96;

    [Flags]
    private enum ResizeEdges
    {
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }

    private void ResetLayout()
    {
        var defaults = new FurniturePrintLayout();
        draft.CopyFrom(defaults);
        LoadFields();
        RefreshPreview();
    }

    private static bool TryRead(TextBox box, out double value)
    {
        value = default;
        return !IsIncompleteNumber(box.Text) &&
               (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value));
    }

    private static bool IsIncompleteNumber(string value)
    {
        var trimmed = value.TrimEnd();
        return trimmed.EndsWith(',') || trimmed.EndsWith('.');
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    private static void AddSectionHeading(Panel panel, string text) =>
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 43, 66)),
            Margin = new Thickness(0, 0, 0, 4)
        });

    private void AddNumericField(
        Grid grid,
        string label,
        TextBox input,
        double step,
        int column,
        int row)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 6) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(67, 87, 106)) });
        input.Tag = step;
        input.MinWidth = 0;
        input.Cursor = Cursors.SizeWE;
        input.ToolTip = "Zahl eingeben, die Pfeile verwenden oder gedrückt horizontal ziehen";
        input.PreviewMouseLeftButtonDown += NumericField_PreviewMouseLeftButtonDown;
        input.PreviewMouseMove += NumericField_PreviewMouseMove;
        input.PreviewMouseLeftButtonUp += NumericField_PreviewMouseLeftButtonUp;
        input.LostMouseCapture += NumericField_LostMouseCapture;

        input.Padding = new Thickness(9, 3, 4, 3);
        var inputGrid = new Grid { Width = 102, Height = 38 };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition());
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        inputGrid.RowDefinitions.Add(new RowDefinition());
        inputGrid.RowDefinitions.Add(new RowDefinition());
        Grid.SetRowSpan(input, 2);
        inputGrid.Children.Add(input);

        var increase = CreateStepperButton("▲", () => AdjustNumericField(input, step));
        Grid.SetColumn(increase, 1);
        Grid.SetRow(increase, 0);
        inputGrid.Children.Add(increase);

        var decrease = CreateStepperButton("▼", () => AdjustNumericField(input, -step));
        Grid.SetColumn(decrease, 1);
        Grid.SetRow(decrease, 1);
        inputGrid.Children.Add(decrease);

        panel.Children.Add(inputGrid);
        Grid.SetColumn(panel, column);
        Grid.SetRow(panel, row);
        grid.Children.Add(panel);
    }

    private static Button CreateStepperButton(string glyph, Action action)
    {
        var button = new Button
        {
            Content = glyph,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 1, 0, 1),
            MinHeight = 0,
            FontSize = 8,
            FontWeight = FontWeights.Bold
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void AdjustNumericField(TextBox box, double amount)
    {
        if (!TryRead(box, out var value))
            return;

        box.Text = Format(value + amount);
        box.Focus();
        box.SelectAll();
    }

    private sealed record LayoutChoice(string Name, string Key)
    {
        public override string ToString() => Name;
    }
}
