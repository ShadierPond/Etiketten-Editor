using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LabelEditor.Desktop;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalStore store;
    private readonly PhotoLibrary photoLibrary = new();
    private readonly bool isFirstRun;
    private bool setupCompleted;
    private bool initializingMainWindow = true;
    private CatalogData data;
    private int catalogIndex;
    private int labelPageIndex;
    private string? labelNumericSortFieldId;
    private ListSortDirection labelNumericSortDirection;
    private double previewWidthRatio = 0.4;
    private bool isUpdatingPageSelectors;
    private bool isSelectingSearchResult;
    private ContextMenu? openActionMenu;
    private ScrollViewer? previewScrollViewer;
    private readonly List<Border> previewLabelCells = [];
    private readonly List<DataGridColumn> labelFieldColumns = [];
    private readonly Dictionary<DataGridColumn, string> labelColumnFieldIds = [];
    private EditorCatalog? labelColumnsCatalog;
    private readonly List<(DataGridColumn Column, DependencyPropertyDescriptor Descriptor, EventHandler Handler)> columnWidthHandlers = [];
    private Border? highlightedPreviewCell;
    private readonly DispatcherTimer previewZoomTimer;
    private readonly DispatcherTimer columnWidthSaveTimer;
    private int previewDocumentRevision;
    private HwndSource? windowSource;
    private UIElement? previewZoomAnchor;
    private Point previewZoomAnchorPoint;
    private Point previewZoomViewportPoint;
    private double pendingPreviewZoomDelta;
    private bool previewZoomRestorePending;
    private CatalogGridRow? labelDragSource;
    private CatalogGridRow? labelDragTarget;
    private CatalogGridRow? labelDropPlaceholder;
    private DragPreviewAdorner? labelDragAdorner;
    private AdornerLayer? labelDragAdornerLayer;
    private int labelDropIndex = -1;
    private Point labelDragStartPoint;
    private readonly DispatcherTimer highlightTimer;
    private readonly CancellationTokenSource updateCheckCancellation = new();

    public List<string> LabelPageNames => CurrentCatalog.Lists.Select(page => page.Name).ToList();
    public List<string> CatalogNames => data.Catalogs.Select(catalog => catalog.Name).ToList();

    public MainWindow(LocalStore store, bool isFirstRun = false)
    {
        AppDiagnostics.Event("mainWindow.construct.start");
        InitializeComponent();
        AppDiagnostics.Event("mainWindow.xaml.initialize.complete");
        AppDiagnostics.StartUiThreadMonitor(Dispatcher);
        PreviewMouseDown += ActionMenu_PreviewMouseDown;
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
        Deactivated += (_, _) =>
        {
            CloseActionMenu();
            CloseSearchResults();
        };
        previewZoomTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        previewZoomTimer.Tick += PreviewZoomTimer_Tick;
        columnWidthSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        columnWidthSaveTimer.Tick += (_, _) =>
        {
            columnWidthSaveTimer.Stop();
            Save();
        };
        highlightTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        highlightTimer.Tick += (_, _) =>
        {
            highlightTimer.Stop();
            foreach (var row in LabelGrid.Items.OfType<CatalogGridRow>())
                row.IsHighlighted = false;
        };
        this.store = store;
        AppDiagnostics.Event("mainWindow.previewWidth.load.start");
        previewWidthRatio = Math.Clamp(store.LoadPreviewWidthRatio(), 0.2, 0.75);
        AppDiagnostics.Event("mainWindow.previewWidth.load.complete");
        EditorColumn.Width = new GridLength(1 - previewWidthRatio, GridUnitType.Star);
        PreviewColumn.Width = new GridLength(previewWidthRatio, GridUnitType.Star);
        this.isFirstRun = isFirstRun;
        setupCompleted = !isFirstRun;
        if (isFirstRun)
        {
            Opacity = 0;
            ShowInTaskbar = false;
        }
        AppDiagnostics.Event("mainWindow.catalog.load.start");
        data = store.LoadCatalog();
        AppDiagnostics.Event("mainWindow.catalog.load.complete");
        AppDiagnostics.Event("mainWindow.photos.normalize.start");
        if (photoLibrary.NormalizeCatalog(data))
            store.SaveCatalog(data);
        AppDiagnostics.Event("mainWindow.photos.normalize.complete");
        DataContext = this;
        AppVersionText.Text = $"Version {GetType().Assembly.GetName().Version?.ToString(3) ?? "unbekannt"}";
        DatabasePathText.Text = store.DatabasePath;
        DatabasePathText.ToolTip = store.DatabasePath;
        AppDiagnostics.Event("mainWindow.pages.refresh.start");
        RefreshPages();
        AppDiagnostics.Event("mainWindow.pages.refresh.complete");
        initializingMainWindow = false;
        Loaded += async (_, _) =>
        {
            if (this.isFirstRun)
            {
                if (!RunCatalogSetup())
                {
                    Close();
                    return;
                }
                Opacity = 1;
                ShowInTaskbar = true;
            }
            await CheckForAvailableUpdateOnStartupAsync();
        };
        Closing += (_, _) =>
        {
            LabelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            PersistLabelColumnWidths();
            columnWidthSaveTimer.Stop();
            if (setupCompleted)
                Save();
        };
    }

    private EditorCatalog CurrentCatalog => data.Catalogs[catalogIndex];
    private CatalogPage CurrentLabelPage => CurrentCatalog.Lists[labelPageIndex];

    private void RefreshPages()
    {
        catalogIndex = Math.Clamp(catalogIndex, 0, data.Catalogs.Count - 1);
        labelPageIndex = Math.Clamp(labelPageIndex, 0, CurrentCatalog.Lists.Count - 1);
        isUpdatingPageSelectors = true;
        CatalogBox.ItemsSource = CatalogNames;
        CatalogBox.SelectedIndex = catalogIndex;
        LabelPageBox.ItemsSource = CurrentCatalog.Lists.Select(page => page.Name).ToList();
        LabelPageBox.SelectedIndex = labelPageIndex;
        isUpdatingPageSelectors = false;
        CatalogHeading.Text = CurrentCatalog.Name;
        CatalogDescription.Text = $"{CurrentCatalog.Lists.Count} Listen · {CurrentCatalog.Layout.Fields.Count} Vorlagenfelder";
        if (initializingMainWindow)
            AppDiagnostics.Event("mainWindow.rows.refresh.start");
        RefreshLabelRows();
        if (initializingMainWindow)
            AppDiagnostics.Event("mainWindow.rows.refresh.complete");
        if (initializingMainWindow)
            AppDiagnostics.Event("mainWindow.preview.refresh.start");
        RefreshPrintPreview();
        if (initializingMainWindow)
            AppDiagnostics.Event("mainWindow.preview.refresh.complete");
        RefreshSearchResults();
    }

    private void RefreshLabelRows()
    {
        if (LabelGrid is null || data.Catalogs.Count == 0 || CurrentCatalog.Lists.Count == 0)
            return;
        var selectedItem = (LabelGrid.SelectedItem as CatalogGridRow)?.Item;
        if (LabelGrid.SelectedItem is not CatalogGridRow selectedRow ||
            !ReferenceEquals(selectedRow.Page, CurrentLabelPage))
            selectedItem = null;
        var highlightedItem = highlightTimer.IsEnabled
            ? LabelGrid.Items.OfType<CatalogGridRow>().FirstOrDefault(row => row.IsHighlighted)?.Item
            : null;
        data.Normalize();
        RefreshLabelFieldColumns();
        var query = SearchBox.Text.Trim();
        var rows = CurrentLabelPage.Items
            .Select((item, index) => CreateLabelGridRow(CurrentLabelPage, item, index))
            .Where(row => Matches(query, CurrentLabelPage.Name, row.Title, row.Beco, row.Ref,
                row.Price, row.Ek, row.SalesPrice, string.Join(" ", row.Item.FieldValues.Values)))
            .ToList();
        if (labelNumericSortFieldId is { } sortFieldId &&
            CurrentCatalog.Layout.Fields.FirstOrDefault(field => field.Id == sortFieldId && field.Kind == "Number") is { } sortField)
        {
            rows.Sort((left, right) => CompareNumericFieldRows(left, right, sortField.Id, labelNumericSortDirection));
        }
        if (highlightedItem is not null)
        {
            var highlightedRow = rows.FirstOrDefault(row => ReferenceEquals(row.Item, highlightedItem));
            if (highlightedRow is not null)
                highlightedRow.IsHighlighted = true;
        }
        LabelGrid.ItemsSource = rows;
        if (selectedItem is not null)
        {
            var selectedGridRow = rows.FirstOrDefault(row => ReferenceEquals(row.Item, selectedItem));
            if (selectedGridRow is not null)
                LabelGrid.SelectedItem = selectedGridRow;
        }
        LabelCountText.Text = $"{CurrentLabelPage.Items.Count} Einträge in „{CurrentLabelPage.Name}“";
    }

    private void LabelGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (labelColumnFieldIds.TryGetValue(e.Column, out var fieldId) &&
            CurrentCatalog.Layout.Fields.FirstOrDefault(field => field.Id == fieldId && field.Kind == "Number") is not null)
        {
            e.Handled = true;
            labelNumericSortDirection = labelNumericSortFieldId == fieldId &&
                labelNumericSortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            labelNumericSortFieldId = fieldId;
            foreach (var column in LabelGrid.Columns)
                column.SortDirection = null;
            e.Column.SortDirection = labelNumericSortDirection;
            RefreshLabelRows();
            return;
        }

        labelNumericSortFieldId = null;
    }

    private static int CompareNumericFieldRows(
        CatalogGridRow left,
        CatalogGridRow right,
        string fieldId,
        ListSortDirection direction)
    {
        var leftValue = left.FieldValues.GetValueOrDefault(fieldId, "");
        var rightValue = right.FieldValues.GetValueOrDefault(fieldId, "");
        var leftHasNumber = TryParseSortableNumber(leftValue, out var leftNumber);
        var rightHasNumber = TryParseSortableNumber(rightValue, out var rightNumber);
        if (leftHasNumber != rightHasNumber)
            return leftHasNumber ? -1 : 1;

        var comparison = leftHasNumber
            ? leftNumber.CompareTo(rightNumber)
            : StringComparer.CurrentCultureIgnoreCase.Compare(leftValue, rightValue);
        if (comparison == 0)
            comparison = left.Index.CompareTo(right.Index);
        return direction == ListSortDirection.Ascending ? comparison : -comparison;
    }

    private static bool TryParseSortableNumber(string value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out number) ||
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private CatalogGridRow CreateLabelGridRow(CatalogPage page, CatalogItem item, int index) =>
        new(
            page,
            item,
            index,
            index + 1,
            labelPageIndex,
            CurrentCatalog.Layout.Fields
                .Where(field => field.Kind == "Photo")
                .ToDictionary(field => field.Id, field => photoLibrary.GetPhotoPath(
                    data, item.FieldValues.GetValueOrDefault(field.Id, item.Photo))));

    private void RefreshLabelFieldColumns()
    {
        PersistLabelColumnWidths();
        foreach (var (column, descriptor, handler) in columnWidthHandlers)
            descriptor.RemoveValueChanged(column, handler);
        columnWidthHandlers.Clear();
        foreach (var column in labelFieldColumns)
        {
            LabelGrid.Columns.Remove(column);
            labelColumnFieldIds.Remove(column);
        }
        labelFieldColumns.Clear();

        var catalog = CurrentCatalog;
        labelColumnsCatalog = catalog;
        foreach (var field in CurrentCatalog.Layout.Fields)
        {
            var hasSavedWidth = catalog.ColumnWidths.TryGetValue(field.Id, out var savedWidth)
                && double.IsFinite(savedWidth)
                && savedWidth >= 40;
            var savedColumnWidth = hasSavedWidth
                ? new DataGridLength(Math.Clamp(savedWidth, 40, 1200))
                : new DataGridLength(1, DataGridLengthUnitType.Star);
            DataGridColumn column;
            if (field.Kind == "Photo")
            {
                var button = new FrameworkElementFactory(typeof(Button));
                button.SetValue(Button.StyleProperty, LabelGrid.Resources["PhotoPickerButton"]);
                button.SetValue(FrameworkElement.TagProperty, field.Id);
                button.SetValue(FrameworkElement.ToolTipProperty, $"Foto für „{field.Name}“ auswählen");
                button.AddHandler(Button.ClickEvent, new RoutedEventHandler(EditRowPhoto_Click));
                var image = new FrameworkElementFactory(typeof(Image));
                image.SetValue(FrameworkElement.WidthProperty, 42d);
                image.SetValue(FrameworkElement.HeightProperty, 32d);
                image.SetValue(Image.StretchProperty, System.Windows.Media.Stretch.Uniform);
                image.SetBinding(Image.SourceProperty, new Binding($"PhotoPaths[{field.Id}]"));
                button.AppendChild(image);
                var template = new DataTemplate { VisualTree = button };
                column = new DataGridTemplateColumn
                {
                    Header = field.Name,
                    Width = hasSavedWidth ? savedColumnWidth : new DataGridLength(82),
                    CellTemplate = template
                };
            }
            else
            {
                column = new DataGridTextColumn
                {
                    Header = field.Name,
                    Binding = new Binding($"FieldValues[{field.Id}]")
                    {
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    },
                    Width = savedColumnWidth,
                    ElementStyle = (Style)LabelGrid.Resources["CenteredCellText"],
                    EditingElementStyle = (Style)LabelGrid.Resources["CenteredCellEditor"]
                };
            }

            LabelGrid.Columns.Add(column);
            labelFieldColumns.Add(column);
            labelColumnFieldIds[column] = field.Id;
            if (field.Id == labelNumericSortFieldId)
                column.SortDirection = labelNumericSortDirection;
            var descriptor = DependencyPropertyDescriptor.FromProperty(
                DataGridColumn.WidthProperty, typeof(DataGridColumn));
            EventHandler handler = (_, _) =>
            {
                var width = column.Width.UnitType == DataGridLengthUnitType.Pixel
                    ? column.Width.Value
                    : column.ActualWidth;
                if (!double.IsFinite(width) || width < 40)
                    return;

                catalog.ColumnWidths[field.Id] = Math.Clamp(width, 40, 1200);
                columnWidthSaveTimer.Stop();
                columnWidthSaveTimer.Start();
            };
            descriptor.AddValueChanged(column, handler);
            columnWidthHandlers.Add((column, descriptor, handler));
        }
    }

    private void PersistLabelColumnWidths()
    {
        if (LabelGrid is null || labelColumnsCatalog is null)
            return;

        var catalog = labelColumnsCatalog;
        foreach (var column in labelFieldColumns)
        {
            if (!labelColumnFieldIds.TryGetValue(column, out var fieldId))
                continue;
            var width = column.Width.UnitType == DataGridLengthUnitType.Pixel
                ? column.Width.Value
                : column.ActualWidth;
            if (double.IsFinite(width) && width >= 40)
                catalog.ColumnWidths[fieldId] = Math.Clamp(width, 40, 1200);
        }
    }

    private static bool Matches(string search, params string?[] fields) =>
        search.Length == 0 || fields.Any(value => value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true);

    private void LabelPageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingPageSelectors || LabelPageBox.SelectedIndex < 0)
            return;
        PersistLabelColumnWidths();
        columnWidthSaveTimer.Stop();
        Save();
        labelPageIndex = LabelPageBox.SelectedIndex;
        RefreshLabelRows();
        RefreshPrintPreview();
    }

    private void CatalogBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingPageSelectors || CatalogBox.SelectedIndex < 0)
            return;
        PersistLabelColumnWidths();
        columnWidthSaveTimer.Stop();
        Save();
        catalogIndex = CatalogBox.SelectedIndex;
        labelPageIndex = 0;
        RefreshPages();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchBoxAffordances();
        RefreshLabelRows();
        RefreshSearchResults();
    }

    private void SearchBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        UpdateSearchBoxAffordances();
        RefreshSearchResults();
    }

    private void SearchBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) =>
        UpdateSearchBoxAffordances();

    private void UpdateSearchBoxAffordances()
    {
        if (SearchBox is null)
            return;

        var hasText = !string.IsNullOrEmpty(SearchBox.Text);
        SearchPlaceholder.Visibility = !hasText && !SearchBox.IsKeyboardFocusWithin
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
        SearchBox.CaretIndex = 0;
    }

    private void SearchBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, RefreshSearchResults);
    }

    private void RefreshSearchResults()
    {
        if (SearchResultsBox is null || isSelectingSearchResult)
            return;
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            SearchResultsPopup.IsOpen = false;
            return;
        }

        var results = data.Catalogs
            .SelectMany((catalog, catalogIndex) => catalog.Lists
                .SelectMany((page, pageIndex) => page.Items.Select((item, itemIndex) =>
                    (Catalog: catalog, CatalogIndex: catalogIndex, Page: page, PageIndex: pageIndex,
                        Item: item, ItemIndex: itemIndex))))
            .Where(result => Matches(query, result.Catalog.Name, result.Page.Name, result.Item.Title, result.Item.Beco,
                result.Item.Ref, result.Item.Price, result.Item.Ek, result.Item.SalesPrice,
                string.Join(" ", result.Item.FieldValues.Values)))
            .Select(result => new SearchResultRow(result.Catalog, result.CatalogIndex, result.Page, result.PageIndex,
                result.Item, result.ItemIndex,
                $"{result.Catalog.Name} · {result.Page.Name} · {result.ItemIndex + 1} · {SearchSummary(result.Item)}"))
            .ToList();
        SearchResultsBox.ItemsSource = results;
        SearchResultsPopup.IsOpen = results.Count > 0 && SearchBox.IsKeyboardFocusWithin;
    }

    private static string SearchSummary(CatalogItem item)
    {
        var summary = string.Join(" · ", new[] { item.Title, item.Beco, item.Ref }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return summary.Length == 0 ? "Leerer Eintrag" : summary;
    }

    private void SearchResultsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isSelectingSearchResult || SearchResultsBox.SelectedItem is not SearchResultRow result)
            return;

        isSelectingSearchResult = true;
        try
        {
            var pageChanged = catalogIndex != result.CatalogIndex || labelPageIndex != result.PageIndex;
            catalogIndex = result.CatalogIndex;
            labelPageIndex = result.PageIndex;
            if (pageChanged)
                RefreshPages();
            LabelPageBox.SelectedIndex = labelPageIndex;
            var row = LabelGrid.Items.OfType<CatalogGridRow>()
                .FirstOrDefault(candidate => ReferenceEquals(candidate.Item, result.Item));
            if (row is not null)
            {
                LabelGrid.SelectedItem = row;
                LabelGrid.ScrollIntoView(row);
                HighlightLabelRow(result.Item);
            }
            SearchResultsBox.SelectedIndex = -1;
            SearchResultsPopup.IsOpen = false;
            UpdateLabelPreviewHighlight();
        }
        finally
        {
            isSelectingSearchResult = false;
        }
    }

    private void LabelGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure(
            "grid.selection",
            () => $"added={e.AddedItems.Count};removed={e.RemovedItems.Count};previewCells={previewLabelCells.Count}");
#endif
        UpdateLabelPreviewHighlight();
    }

    private void PreviewLabelItem_Clicked(CatalogItem item)
    {
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure(
            "preview.cell.click",
            () => $"position={CurrentLabelPage.Items.IndexOf(item) + 1}");
#endif
        SearchBox.Clear();
        var row = LabelGrid.Items.OfType<CatalogGridRow>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Item, item));
        if (row is null)
            return;
        LabelGrid.SelectedItem = row;
        LabelGrid.ScrollIntoView(row);
        HighlightLabelRow(item);
        UpdateLabelPreviewHighlight();
    }

    private void HighlightLabelRow(CatalogItem item)
    {
        highlightTimer.Stop();
        CatalogGridRow? target = null;
        foreach (var row in LabelGrid.Items.OfType<CatalogGridRow>())
        {
            row.IsHighlighted = ReferenceEquals(row.Item, item);
            if (row.IsHighlighted)
                target = row;
        }
        if (target is null)
            return;
        highlightTimer.Start();
    }

    private void EditRowPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogGridRow row })
            return;
        LabelGrid.SelectedItem = row;
        EditLabelPhoto(row.Page, row.ItemIndex, (sender as FrameworkElement)?.Tag as string);
    }

    private void EditLabelPhoto(CatalogPage page, int itemIndex, string? fieldId = null)
    {
        LabelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LabelGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var picker = new PhotoLibraryWindow(data, photoLibrary, PersistCatalogChanges) { Owner = this };
        picker.Closed += (_, _) =>
        {
            if (!picker.HasChanges)
                return;
            Save();
            RefreshLabelRows();
            RefreshPrintPreview();
        };
        if (picker.ShowDialog() != true || picker.SelectedPhotoKey is null)
            return;

        var item = page.Items[itemIndex];
        if (!string.IsNullOrWhiteSpace(fieldId))
        {
            item.FieldValues[fieldId] = picker.SelectedPhotoKey;
            var field = CurrentCatalog.Layout.Fields.FirstOrDefault(candidate => candidate.Id == fieldId);
            if (field?.Source == "Photo")
                item.Photo = picker.SelectedPhotoKey;
        }
        else
        {
            item.Photo = picker.SelectedPhotoKey;
        }
        Save();
        RefreshLabelRows();
        RefreshPrintPreview();
    }

    private void MoveLabelUp_Click(object sender, RoutedEventArgs e) => MoveSelectedLabel(-1);

    private void MoveLabelDown_Click(object sender, RoutedEventArgs e) => MoveSelectedLabel(1);

    private void MoveLabelTo_Click(object sender, RoutedEventArgs e)
    {
        if (LabelGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wählen Sie zuerst einen Eintrag aus.", "Eintrag verschieben",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var target = TextPromptWindow.Show(this, "Eintrag verschieben",
            $"Neue Position (1–{CurrentLabelPage.Items.Count})", (row.ItemIndex + 1).ToString());
        if (target is null)
            return;
        if (!int.TryParse(target, out var targetPosition) || targetPosition is < 1 || targetPosition > CurrentLabelPage.Items.Count)
        {
            MessageBox.Show($"Geben Sie eine Position von 1 bis {CurrentLabelPage.Items.Count} ein.", "Eintrag verschieben",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MoveLabelTo(row, targetPosition - 1);
    }

    private void LabelGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var cell = FindVisualAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell?.Column.DisplayIndex != 0 ||
            cell.DataContext is not CatalogGridRow { IsDropPlaceholder: false } row)
            return;

        labelDragSource = row;
        labelDragStartPoint = e.GetPosition(LabelGrid);
    }

    private void LabelGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (labelDragSource is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pointer = e.GetPosition(LabelGrid);
        if (Math.Abs(pointer.X - labelDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pointer.Y - labelDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var source = labelDragSource;
        LabelGrid.SelectedItem = source;
        LabelGrid.Cursor = Cursors.SizeAll;
        StartLabelDragPreview(source);
        DragDrop.DoDragDrop(LabelGrid, source, DragDropEffects.Move);
        ResetLabelDrag();
        e.Handled = true;
    }

    private void LabelGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ResetLabelDrag();
    }

    private void LabelGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var dataGridRow = FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (dataGridRow?.Item is not CatalogGridRow row)
        {
            e.Handled = true;
            return;
        }

        LabelGrid.SelectedItem = row;
        var itemIndex = row.Page.Items.IndexOf(row.Item);
        MoveLabelUpMenuItem.IsEnabled = itemIndex > 0;
        MoveLabelDownMenuItem.IsEnabled = itemIndex >= 0 && itemIndex < row.Page.Items.Count - 1;
        SaveItemCodeMenuItem.Header = PrintService.IsBarcodeField(CurrentCatalog.Layout)
            ? "Barcode speichern…"
            : "QR-Code speichern…";
        SaveItemCodeMenuItem.IsEnabled =
            !string.IsNullOrWhiteSpace(PrintService.GetQrContent(row.Item, CurrentCatalog.Layout));
        DownloadItemPhotoMenuItem.IsEnabled = FindItemPhotoPath(row.Item) is not null;
    }

    private void LabelGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(CatalogGridRow)) is not CatalogGridRow source)
            return;

        var targetRow = FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        var target = targetRow?.Item as CatalogGridRow;
        var targetIndex = GetLabelDropIndex(source, targetRow, e.GetPosition(LabelGrid),
            targetRow is null ? default : e.GetPosition(targetRow));
        var canDrop = targetIndex >= 0;

        SetLabelDragTarget(canDrop && target is { IsDropPlaceholder: false } ? target : null);
        if (canDrop)
        {
            UpdateLabelDragPreview(source, targetIndex);
            labelDragAdorner?.SetPosition(e.GetPosition(LabelGrid));
        }
        else
        {
            labelDropIndex = -1;
        }
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void LabelGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(CatalogGridRow)) is not CatalogGridRow source)
            return;

        var targetRow = FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        var targetIndex = GetLabelDropIndex(source, targetRow, e.GetPosition(LabelGrid),
            targetRow is null ? default : e.GetPosition(targetRow));

        ResetLabelDrag();
        if (targetIndex >= 0)
        {
            MoveLabelTo(source, targetIndex);
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private int GetLabelDropIndex(
        CatalogGridRow source,
        DataGridRow? targetRow,
        Point gridPosition,
        Point rowPosition)
    {
        if (targetRow is null)
        {
            var headerHeight = FindVisualChild<DataGridColumnHeadersPresenter>(LabelGrid)?.ActualHeight ?? 0;
            return gridPosition.Y >= headerHeight && gridPosition.Y <= LabelGrid.ActualHeight
                ? source.Page.Items.Count - 1
                : -1;
        }

        if (targetRow.Item is not CatalogGridRow target ||
            !ReferenceEquals(target.Page, source.Page))
            return -1;

        if (target.IsDropPlaceholder)
            return target.DropIndex;
        if (ReferenceEquals(target.Item, source.Item))
            return -1;

        var sourceIndex = source.Page.Items.IndexOf(source.Item);
        var targetIndex = source.Page.Items.IndexOf(target.Item);
        if (sourceIndex < 0 || targetIndex < 0)
            return -1;

        var dropAfter = rowPosition.Y >= targetRow.ActualHeight / 2;
        var insertionIndex = sourceIndex < targetIndex
            ? targetIndex - (dropAfter ? 0 : 1)
            : targetIndex + (dropAfter ? 1 : 0);
        return Math.Clamp(insertionIndex, 0, source.Page.Items.Count - 1);
    }

    private void StartLabelDragPreview(CatalogGridRow source)
    {
        if (LabelGrid.ItemContainerGenerator.ContainerFromItem(source) is not DataGridRow sourceRow ||
            sourceRow.ActualWidth <= 0 ||
            sourceRow.ActualHeight <= 0)
            return;

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(sourceRow.ActualWidth),
            (int)Math.Ceiling(sourceRow.ActualHeight),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(sourceRow);
        bitmap.Freeze();

        labelDragAdornerLayer = AdornerLayer.GetAdornerLayer(LabelGrid);
        if (labelDragAdornerLayer is null)
            return;

        labelDragAdorner = new DragPreviewAdorner(LabelGrid, bitmap);
        labelDragAdornerLayer.Add(labelDragAdorner);
        labelDragAdorner.SetPosition(Mouse.GetPosition(LabelGrid));
        labelDropIndex = source.ItemIndex;
        UpdateLabelDragPreview(source, labelDropIndex);
    }

    private void UpdateLabelDragPreview(CatalogGridRow source, int insertionIndex)
    {
        if (labelDropPlaceholder is not null && labelDropIndex == insertionIndex)
            return;

        labelDropIndex = insertionIndex;
        labelDropPlaceholder = new CatalogGridRow(
            source.Page,
            new CatalogItem(),
            insertionIndex,
            insertionIndex + 1,
            labelPageIndex,
            new Dictionary<string, string?>())
        {
            IsDropPlaceholder = true,
            DropIndex = insertionIndex
        };

        var rows = source.Page.Items
            .Select((item, index) => (item, index))
            .Where(entry => !ReferenceEquals(entry.item, source.Item))
            .Select(entry => CreateLabelGridRow(source.Page, entry.item, entry.index))
            .ToList();
        rows.Insert(Math.Clamp(insertionIndex, 0, rows.Count), labelDropPlaceholder);
        LabelGrid.ItemsSource = rows;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
                return match;

            element = element switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(element),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return null;
    }

    private void SetLabelDragTarget(CatalogGridRow? target)
    {
        if (ReferenceEquals(labelDragTarget, target))
            return;

        if (labelDragTarget is not null)
            labelDragTarget.IsDragTarget = false;

        labelDragTarget = target;
        if (labelDragTarget is not null)
            labelDragTarget.IsDragTarget = true;
    }

    private void ResetLabelDrag()
    {
        SetLabelDragTarget(null);
        if (labelDragAdorner is not null && labelDragAdornerLayer is not null)
            labelDragAdornerLayer.Remove(labelDragAdorner);
        labelDragAdorner = null;
        labelDragAdornerLayer = null;
        var hadDropPreview = labelDropPlaceholder is not null;
        labelDropPlaceholder = null;
        labelDropIndex = -1;
        labelDragSource = null;
        LabelGrid.Cursor = null;
        if (hadDropPreview && data.Catalogs.Count > 0)
            RefreshLabelRows();
    }

    private void MoveSelectedLabel(int direction)
    {
        if (LabelGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wählen Sie zuerst einen Eintrag aus.", "Eintrag verschieben",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var currentIndex = row.Page.Items.IndexOf(row.Item);
        MoveLabelTo(row, currentIndex + direction);
    }

    private void MoveLabelTo(CatalogGridRow row, int targetIndex)
    {
        var currentIndex = row.Page.Items.IndexOf(row.Item);
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= row.Page.Items.Count)
            return;
        if (targetIndex == currentIndex)
            return;

        LabelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LabelGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var item = row.Page.Items[currentIndex];
        row.Page.Items.RemoveAt(currentIndex);
        row.Page.Items.Insert(targetIndex, item);
        RefreshLabelRows();
        var movedRow = LabelGrid.Items.OfType<CatalogGridRow>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Item, row.Item));
        if (movedRow is not null)
        {
            LabelGrid.SelectedItem = movedRow;
            LabelGrid.ScrollIntoView(movedRow);
        }
        Save();
        RefreshPrintPreview();
    }

    private void EditLabelLayout_Click(object sender, RoutedEventArgs e)
    {
        PersistLabelColumnWidths();
        var sample = LabelGrid.SelectedItem is CatalogGridRow selected ? selected.Item : CurrentLabelPage.Items[0];
        var oldFieldIds = CurrentCatalog.Layout.Fields.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
        var editor = new LabelLayoutWindow(data, sample, photoLibrary, CurrentCatalog.Layout) { Owner = this };
        if (editor.ShowDialog() != true)
            return;

        var targetEntryCount = editor.Result.TileCount;
        var entriesToRemove = CurrentCatalog.Lists
            .SelectMany(page => page.Items.Skip(targetEntryCount))
            .Count(HasLabelData);
        if (entriesToRemove > 0 &&
            !ConfirmationPromptWindow.Show(
                this,
                "Einträge entfernen?",
                $"Die Anzahl der Einträge je Liste wird auf {targetEntryCount} reduziert. " +
                $"Dabei werden {entriesToRemove} ausgefüllte Einträge am Ende der Listen entfernt. " +
                "Möchten Sie fortfahren?",
                "Einträge entfernen"))
            return;

        if (editor.PendingCompanyLogoPath is { } logoPath)
        {
            try
            {
                editor.Result.LogoPhotoKey = photoLibrary.AddPhoto(logoPath, data);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException or ArgumentException)
            {
                MessageBox.Show(this, $"Das Firmenlogo konnte nicht gespeichert werden.\n\n{ex.Message}",
                    "Katalogvorlage", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        CurrentCatalog.Layout.CopyFrom(editor.Result);
        foreach (var page in CurrentCatalog.Lists)
        {
            if (page.Items.Count > targetEntryCount)
                page.Items.RemoveRange(targetEntryCount, page.Items.Count - targetEntryCount);
            while (page.Items.Count < targetEntryCount)
                page.Items.Add(new CatalogItem());
        }

        var currentFields = CurrentCatalog.Layout.Fields;
        var currentFieldIds = currentFields.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in CurrentCatalog.Lists.SelectMany(page => page.Items))
        {
            foreach (var removedId in oldFieldIds.Except(currentFieldIds))
                item.FieldValues.Remove(removedId);
            foreach (var field in currentFields)
                item.FieldValues.TryAdd(field.Id, "");
        }
        Save();
        RefreshLabelRows();
        RefreshPrintPreview();
    }

    private static bool HasLabelData(CatalogItem item) =>
        !string.IsNullOrWhiteSpace(item.Title) ||
        !string.IsNullOrWhiteSpace(item.Beco) ||
        !string.IsNullOrWhiteSpace(item.Ref) ||
        !string.IsNullOrWhiteSpace(item.Photo) ||
        !string.IsNullOrWhiteSpace(item.Price) ||
        !string.IsNullOrWhiteSpace(item.Ek) ||
        !string.IsNullOrWhiteSpace(item.SalesPrice) ||
        !string.IsNullOrWhiteSpace(item.RecommendedPrice) ||
        item.FieldValues.Values.Any(value => !string.IsNullOrWhiteSpace(value)) ||
        item.AdditionalData is { Count: > 0 };

    private void LabelGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditingElement is TextBox textBox)
        {
            textBox.PreviewTextInput -= NumberField_PreviewTextInput;
            DataObject.RemovePastingHandler(textBox, NumberField_Pasting);
        }

        Dispatcher.BeginInvoke(() =>
        {
            Save();
            RefreshPrintPreview();
        }, DispatcherPriority.Background);
    }

    private void LabelGrid_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is not TextBox textBox ||
            !labelColumnFieldIds.TryGetValue(e.Column, out var fieldId) ||
            CurrentCatalog.Layout.Fields.FirstOrDefault(field => field.Id == fieldId)?.Kind != "Number")
            return;

        textBox.PreviewTextInput += NumberField_PreviewTextInput;
        DataObject.AddPastingHandler(textBox, NumberField_Pasting);
        textBox.Tag = fieldId;
    }

    private void NumberField_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        e.Handled = !IsValidNumberFieldReplacement(
            textBox.Text,
            textBox.SelectionStart,
            textBox.SelectionLength,
            e.Text);
    }

    private void NumberField_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        var pastedText = e.DataObject.GetData(DataFormats.UnicodeText) as string
            ?? e.DataObject.GetData(DataFormats.Text) as string
            ?? "";
        if (!IsValidNumberFieldReplacement(
                textBox.Text,
                textBox.SelectionStart,
                textBox.SelectionLength,
                pastedText))
            e.CancelCommand();
    }

    private static bool IsValidNumberFieldReplacement(
        string currentText,
        int selectionStart,
        int selectionLength,
        string replacement)
    {
        var candidate = currentText.Remove(selectionStart, selectionLength)
            .Insert(selectionStart, replacement);
        var decimalSeparators = 0;
        foreach (var character in candidate)
        {
            if (char.IsDigit(character))
                continue;
            if (character == ',' && ++decimalSeparators <= 1)
                continue;
            return false;
        }

        return true;
    }

    private bool Save()
    {
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure("catalog.save");
#endif
        try
        {
            store.SaveCatalog(data);
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Exception("catalog.save", ex);
            MessageBox.Show($"Die Änderungen konnten nicht gespeichert werden.\n\n{ex.Message}",
                "Speicherfehler", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void PersistCatalogChanges() => Save();

    private void AddLabelPage_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Show(this, "Neue Liste", "Name der Liste", "Neue Liste");
        if (name is null)
            return;
        CurrentCatalog.Lists.Add(CreateNewList(NonEmptyName(name, "Neue Liste"), CurrentCatalog.Layout.TileCount));
        labelPageIndex = CurrentCatalog.Lists.Count - 1;
        RefreshPages();
        Save();
    }

    private void RenameLabelPage_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Show(this, "Liste umbenennen", "Neuer Listenname", CurrentLabelPage.Name);
        if (name is null)
            return;
        CurrentLabelPage.Name = NonEmptyName(name, CurrentLabelPage.Name);
        RefreshPages();
        Save();
    }

    private void DeleteLabelPage_Click(object sender, RoutedEventArgs e)
    {
        var replacesLastPage = CurrentCatalog.Lists.Count == 1;
        var replacementMessage = replacesLastPage ? "\nEine leere Liste wird als Ersatz angelegt." : "";
        if (!ConfirmationPromptWindow.Show(
                this,
                "Liste löschen",
                $"Liste „{CurrentLabelPage.Name}“ einschließlich aller Einträge löschen?{replacementMessage}",
                "Löschen"))
            return;

        if (replacesLastPage)
        {
            CurrentCatalog.Lists[labelPageIndex] = CreateNewList(CreateUnusedLabelPageName(), CurrentCatalog.Layout.TileCount);
        }
        else
        {
            CurrentCatalog.Lists.RemoveAt(labelPageIndex);
            labelPageIndex = Math.Min(labelPageIndex, CurrentCatalog.Lists.Count - 1);
        }
        RefreshPages();
        Save();
    }

    private string CreateUnusedLabelPageName()
    {
        const string baseName = "Neue Liste";
        var existingNames = CurrentCatalog.Lists
            .Where((_, index) => index != labelPageIndex)
            .Select(page => page.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!existingNames.Contains(baseName))
            return baseName;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!existingNames.Contains(candidate))
                return candidate;
        }
    }

    private static CatalogPage CreateNewList(string name, int itemCount) => new()
    {
        Name = name,
        Items = Enumerable.Range(0, Math.Clamp(itemCount, 1, 120))
            .Select(_ => new CatalogItem())
            .ToList()
    };

    private void AddCatalog_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Show(this, "Neuen Katalog", "Name des Katalogs", "Neuer Katalog");
        if (name is null)
            return;

        var layout = new LabelPrintLayout();
        layout.Normalize();
        var catalog = new EditorCatalog
        {
            Name = NonEmptyName(name, "Neuer Katalog"),
            Layout = layout,
            Lists = [CreateNewList("Liste 1", layout.TileCount)]
        };
        data.Catalogs.Add(catalog);
        catalogIndex = data.Catalogs.Count - 1;
        labelPageIndex = 0;
        RefreshPages();
        Save();
    }

    private void RenameCatalog_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Show(this, "Katalog umbenennen", "Neuer Katalogname", CurrentCatalog.Name);
        if (name is null)
            return;
        CurrentCatalog.Name = NonEmptyName(name, CurrentCatalog.Name);
        RefreshPages();
        Save();
    }

    private void DeleteCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (data.Catalogs.Count <= 1)
        {
            MessageBox.Show("Mindestens ein Katalog muss bestehen bleiben.", "Katalog löschen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ConfirmationPromptWindow.Show(
                this,
                "Katalog löschen",
                $"Katalog „{CurrentCatalog.Name}“ einschließlich aller Listen und Einträge löschen?",
                "Löschen"))
            return;

        data.Catalogs.RemoveAt(catalogIndex);
        catalogIndex = Math.Min(catalogIndex, data.Catalogs.Count - 1);
        labelPageIndex = 0;
        RefreshPages();
        Save();
    }

    private void ResetItem_Click(object sender, RoutedEventArgs e)
    {
        if (LabelGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wählen Sie zuerst einen Eintrag aus.", "Eintrag zurücksetzen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ConfirmationPromptWindow.Show(
                this, "Eintrag zurücksetzen", $"Eintrag {row.Index} zurücksetzen?", "Zurücksetzen"))
            return;
        CurrentLabelPage.Items[row.Index - 1] = new CatalogItem();
        RefreshLabelRows();
        RefreshPrintPreview();
        Save();
    }

    private void DeleteLabelItem_Click(object sender, RoutedEventArgs e)
    {
        if (LabelGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wählen Sie zuerst einen Artikel aus.", "Artikel löschen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LabelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LabelGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var itemIndex = row.Page.Items.IndexOf(row.Item);
        if (itemIndex < 0)
            return;

        if (!ConfirmationPromptWindow.Show(
                this,
                "Eintrag löschen",
                $"Eintrag an Position {itemIndex + 1} löschen? Die folgenden Einträge rücken auf. Am Ende wird ein leerer Platz ergänzt.",
                "Löschen"))
            return;

        row.Page.Items.RemoveAt(itemIndex);
        row.Page.Items.Add(new CatalogItem());
        RefreshLabelRows();

        var nextItem = row.Page.Items[Math.Min(itemIndex, row.Page.Items.Count - 1)];
        var nextRow = LabelGrid.Items.OfType<CatalogGridRow>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Item, nextItem));
        if (nextRow is not null)
        {
            LabelGrid.SelectedItem = nextRow;
            LabelGrid.ScrollIntoView(nextRow);
        }

        Save();
        RefreshPrintPreview();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Katalog importieren",
            Filter = "JSON-Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (!ConfirmationPromptWindow.Show(
                this, "Katalog importieren", "Der aktuelle Katalog wird ersetzt. Fortfahren?", "Ersetzen"))
            return;

        TryImportCatalog(dialog.FileName);
    }

    private bool RunCatalogSetup()
    {
        var setup = new CatalogSetupWindow(photoLibrary) { Owner = this };
        if (setup.ShowDialog() != true)
            return false;

        data = setup.Result;
        catalogIndex = 0;
        labelPageIndex = 0;
        if (!Save())
            return false;

        setupCompleted = true;
        RefreshPages();
        return true;
    }

    private void ResetToFactorySettings_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmationPromptWindow.Show(
                this,
                "Auf Werkseinstellungen zurücksetzen",
                "Alle Kataloge, Listen, Einträge und Fotos werden aus der Anwendung entfernt. " +
                "Vor dem Zurücksetzen wird automatisch eine Sicherung inklusive Fotos erstellt. " +
                "Danach beginnt die Einrichtung eines neuen ersten Katalogs.",
                "Zurücksetzen"))
            return;

        string backupPath;
        try
        {
            var backup = photoLibrary.CreateExportCopy(data);
            backupPath = store.BackupAndResetCatalog(JsonSerializer.Serialize(backup, JsonOptions));
        }
        catch (Exception ex)
        {
            AppDiagnostics.Exception("catalog.reset.backup", ex);
            MessageBox.Show($"Die Sicherung oder das Zurücksetzen der Datenbank ist fehlgeschlagen. " +
                            $"Der Einrichtungsvorgang wurde abgebrochen.\n\n{ex.Message}",
                "Zurücksetzen fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string? photoCleanupError = null;
        try
        {
            photoLibrary.Clear();
        }
        catch (Exception ex)
        {
            photoCleanupError = ex.Message;
            AppDiagnostics.Exception("catalog.reset.photos", ex);
        }

        setupCompleted = false;
        data = CatalogData.CreateDefault();
        if (!RunCatalogSetup())
        {
            MessageBox.Show(this, $"Die Datenbank wurde zurückgesetzt. Die Sicherung liegt hier:\n{backupPath}",
                "Einrichtung nicht abgeschlossen", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
            return;
        }

        var message = $"Die Datenbank wurde zurückgesetzt. Eine Sicherung liegt hier:\n{backupPath}";
        if (photoCleanupError is not null)
            message += $"\n\nEinige lokale Fotodateien konnten nicht entfernt werden:\n{photoCleanupError}";
        MessageBox.Show(this, message, "Einrichtung abgeschlossen",
            MessageBoxButton.OK,
            photoCleanupError is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void TryImportCatalog(string fileName)
    {
        try
        {
            var imported = JsonSerializer.Deserialize<CatalogData>(File.ReadAllText(fileName), JsonOptions)
                ?? throw new InvalidDataException("Die Datei enthält keinen gültigen Katalog.");
            if ((imported.Catalogs is null || imported.Catalogs.Count == 0) &&
                (imported.Pages is null || imported.Pages.Count == 0))
                throw new InvalidDataException("Die Datei enthält keine Kataloge oder Listen.");
            if (imported.AdditionalData is { } additionalData &&
                additionalData.TryGetValue("employees", out var legacyEmployees) &&
                legacyEmployees.ValueKind == JsonValueKind.Array)
            {
                imported.EmployeePages =
                [
                    new EmployeePage
                    {
                        Name = "Filiale 1",
                        Employees = JsonSerializer.Deserialize<List<Employee>>(legacyEmployees.GetRawText(), JsonOptions) ?? []
                    }
                ];
                additionalData.Remove("employees");
            }
            imported.Normalize();
            var importedCatalogs = imported.Catalogs;
            if (importedCatalogs is null || importedCatalogs.Count == 0 ||
                importedCatalogs.Any(catalog => catalog is null || catalog.Lists is null || catalog.Lists.Count == 0))
                throw new InvalidDataException("Die Datei enthält keinen gültigen Katalog mit mindestens einer Liste.");
            var skippedPhotos = photoLibrary.NormalizeCatalogForImport(
                imported, Path.GetDirectoryName(fileName));
            data = imported;
            catalogIndex = 0;
            labelPageIndex = 0;
            RefreshPages();
            Save();
            if (skippedPhotos > 0)
            {
                MessageBox.Show(
                    $"Der Katalog wurde importiert. {skippedPhotos} Foto(s) in einem nicht unterstützten Format wurden ausgelassen; die Artikeldaten wurden übernommen.",
                    "Import abgeschlossen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Die Datei konnte nicht importiert werden.\n\n{ex.Message}",
                "Importfehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Katalog exportieren",
            Filter = "JSON-Dateien (*.json)|*.json",
            FileName = "labeln-daten.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            Save();
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(photoLibrary.CreateExportCopy(data), JsonOptions));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Der Katalog konnte nicht exportiert werden.\n\n{ex.Message}",
                "Exportfehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        new UpdateWindow(checkForUpdatesOnOpen: true) { Owner = this }.ShowDialog();
    }

    private async Task CheckForAvailableUpdateOnStartupAsync()
    {
        var updater = new GitHubUpdateService();
        try
        {
            var token = await updater.GetTokenAsync(updateCheckCancellation.Token);
            var release = await updater.GetLatestReleaseAsync(token, updateCheckCancellation.Token);
            var currentVersion = GetType().Assembly.GetName().Version;
            if (!GitHubUpdateService.IsNewerVersion(release.Version, currentVersion))
                return;

            var result = MessageBox.Show(
                $"Version {release.Version} ist verfügbar.\n\n" +
                "Möchtest du das Update jetzt herunterladen und zur Installation vorbereiten?",
                "Etiketten Editor – Update verfügbar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
                new UpdateWindow(checkForUpdatesOnOpen: true) { Owner = this }.ShowDialog();
        }
        catch (OperationCanceledException) when (updateCheckCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AppDiagnostics.Exception("update.startup_check", exception);
        }
    }

    private void PrintCurrent_Click(object sender, RoutedEventArgs e)
    {
        LabelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        PrintService.PrintLabels(this, data, [CurrentLabelPage], photoLibrary, CurrentCatalog.Layout, CurrentCatalog.Name);
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        new HelpWindow { Owner = this }.ShowDialog();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F1)
            return;

        Help_Click(this, new RoutedEventArgs());
        e.Handled = true;
    }

    private void PrintAll_Click(object sender, RoutedEventArgs e)
    {
        LabelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        PrintService.PrintLabels(this, data, CurrentCatalog.Lists, photoLibrary, CurrentCatalog.Layout,
            CurrentCatalog.Name, showPageNumbers: true);
    }

    private void ShowActionMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;

        if (ReferenceEquals(openActionMenu, menu))
        {
            CloseActionMenu();
            return;
        }

        CloseActionMenu();

        menu.Width = double.NaN;
        menu.MinWidth = button.ActualWidth;
        menu.MaxWidth = Math.Max(button.ActualWidth, SystemParameters.WorkArea.Width - 24);
        menu.MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height * 0.75);
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        openActionMenu = menu;
        menu.IsOpen = true;
    }

    private void ActionMenu_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (openActionMenu?.PlacementTarget is Button placementTarget)
        {
            var clickedButton = FindVisualAncestor<Button>(source);
            if (ReferenceEquals(clickedButton, placementTarget))
            {
                CloseActionMenu();
                e.Handled = true;
            }
            else if (!ReferenceEquals(clickedButton?.ContextMenu, openActionMenu))
            {
                CloseActionMenu();
            }
        }

        var clickedOutsideSearch = source is not null &&
            !ReferenceEquals(source, SearchBoxContainer) &&
            !SearchBoxContainer.IsAncestorOf(source);
        if (clickedOutsideSearch)
        {
            if (SearchBox.IsKeyboardFocusWithin)
                Keyboard.ClearFocus();
            if (SearchResultsPopup.IsOpen)
                CloseSearchResults();
        }
    }

    private void CloseSearchResults()
    {
        if (SearchResultsPopup.IsOpen)
            SearchResultsPopup.IsOpen = false;
    }

    private void CloseActionMenu()
    {
        var menu = openActionMenu;
        openActionMenu = null;
        if (menu?.IsOpen == true)
            menu.IsOpen = false;
    }

    private void ActionMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (ReferenceEquals(openActionMenu, menu) && !menu.IsOpen)
                openActionMenu = null;
        });
    }

    private void SaveCurrentAsPdf_Click(object sender, RoutedEventArgs e)
    {
        LabelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        PrintService.SaveLabelsPdf(this, data, [CurrentLabelPage], photoLibrary, CurrentLabelPage.Name,
            CurrentCatalog.Layout, CurrentCatalog.Name);
    }

    private void SaveAllAsPdf_Click(object sender, RoutedEventArgs e)
    {
        LabelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        PrintService.SaveLabelsPdf(this, data, CurrentCatalog.Lists, photoLibrary,
            $"alle-listen-{CurrentCatalog.Name}", CurrentCatalog.Layout, CurrentCatalog.Name,
            showPageNumbers: true);
    }

    private void SaveItemCode_Click(object sender, RoutedEventArgs e)
    {
        if (LabelGrid.SelectedItem is not CatalogGridRow row)
            return;

        LabelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LabelGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var content = PrintService.GetQrContent(row.Item, CurrentCatalog.Layout);
        var image = PrintService.CreateQrBarcodeBytes(row.Item, CurrentCatalog.Layout);
        if (image.Length == 0)
        {
            MessageBox.Show(this, "Für diesen Eintrag ist kein QR-/Barcodewert vorhanden.",
                "Code speichern", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = PrintService.IsBarcodeField(CurrentCatalog.Layout) ? "Barcode speichern" : "QR-Code speichern",
            Filter = "PNG-Bild (*.png)|*.png",
            FileName = $"{SafeFileName(content)}.png",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, image);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Der Code konnte nicht gespeichert werden.\n\n{ex.Message}",
                "Speicherfehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DownloadItemPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (LabelGrid.SelectedItem is not CatalogGridRow row)
            return;

        var photoPath = FindItemPhotoPath(row.Item);
        if (photoPath is null)
        {
            MessageBox.Show(this, "Für diesen Eintrag ist kein verfügbares Bild vorhanden.",
                "Bild herunterladen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var key = CurrentCatalog.Layout.Fields
            .Where(field => field.Kind == "Photo")
            .Select(field => row.Item.FieldValues.GetValueOrDefault(field.Id, ""))
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) &&
                string.Equals(photoLibrary.GetPhotoPath(data, candidate), photoPath, StringComparison.OrdinalIgnoreCase))
            ?? row.Item.Photo;
        data.PhotoNames.TryGetValue(key, out var photoName);
        var extension = Path.GetExtension(photoPath);
        var fileName = $"{SafeFileName(Path.GetFileNameWithoutExtension(photoName ?? photoPath))}{extension}";
        var dialog = new SaveFileDialog
        {
            Title = "Bild herunterladen",
            Filter = "Bilddateien (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.svg)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.svg|Alle Dateien (*.*)|*.*",
            FileName = fileName,
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.Copy(photoPath, dialog.FileName, overwrite: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Das Bild konnte nicht gespeichert werden.\n\n{ex.Message}",
                "Speicherfehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string? FindItemPhotoPath(CatalogItem item)
    {
        foreach (var field in CurrentCatalog.Layout.Fields.Where(field => field.Kind == "Photo"))
        {
            var key = item.FieldValues.GetValueOrDefault(field.Id, "");
            if (string.IsNullOrWhiteSpace(key))
                key = item.Photo;
            var path = photoLibrary.GetPhotoPath(data, key);
            if (path is not null)
                return path;
        }
        return photoLibrary.GetPhotoPath(data, item.Photo);
    }

    private static string SafeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var name = new string(value.Select(character =>
            invalidCharacters.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(name) ? "Artikel" : name;
    }

    private void ChoosePhoto_Click(object sender, RoutedEventArgs e)
    {
        if (LabelGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wähle zuerst einen Eintrag in der Liste aus.", "Foto auswählen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new PhotoLibraryWindow(data, photoLibrary, PersistCatalogChanges) { Owner = this };
        picker.Closed += (_, _) =>
        {
            if (!picker.HasChanges)
                return;
            Save();
            RefreshLabelRows();
            RefreshPrintPreview();
        };
        if (picker.ShowDialog() != true || picker.SelectedPhotoKey is null)
            return;

        var item = row.Item;
        item.Photo = picker.SelectedPhotoKey;
        var photoField = CurrentCatalog.Layout.Fields.FirstOrDefault(field => field.Kind == "Photo");
        if (photoField is not null)
            item.FieldValues[photoField.Id] = picker.SelectedPhotoKey;
        Save();
        RefreshLabelRows();
        RefreshPrintPreview();
    }

    private void TogglePreview_Click(object sender, RoutedEventArgs e)
    {
        var showPreview = PreviewPanel.Visibility != Visibility.Visible;
        if (!showPreview)
            SavePreviewWidthRatio();

        PreviewPanel.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitter.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        EditorColumn.Width = showPreview
            ? new GridLength(1 - previewWidthRatio, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        PreviewSplitterColumn.Width = showPreview ? new GridLength(6) : new GridLength(0);
        PreviewColumn.Width = showPreview
            ? new GridLength(previewWidthRatio, GridUnitType.Star)
            : new GridLength(0);
        PreviewToggleIcon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(
            showPreview
                ? "pack://application:,,,/assets/icons/eye-closed.png"
                : "pack://application:,,,/assets/icons/eye.png"));
        var actionName = showPreview ? "Vorschau ausblenden" : "Vorschau anzeigen";
        PreviewToggleButton.ToolTip = actionName;
        System.Windows.Automation.AutomationProperties.SetName(PreviewToggleButton, actionName);
        if (showPreview)
            RefreshPrintPreview();
    }

    private void PreviewSplitter_DragCompleted(object sender, DragCompletedEventArgs e) =>
        SavePreviewWidthRatio();

    private void SavePreviewWidthRatio()
    {
        var availableWidth = EditorColumn.ActualWidth + PreviewColumn.ActualWidth;
        if (availableWidth <= 0 || PreviewColumn.ActualWidth <= 0)
            return;

        previewWidthRatio = Math.Clamp(PreviewColumn.ActualWidth / availableWidth, 0.2, 0.75);
        store.SavePreviewWidthRatio(previewWidthRatio);
    }

    private void RefreshPrintPreview()
    {
        if (PrintPreviewViewer is null || data.Catalogs.Count == 0 || CurrentCatalog.Lists.Count == 0)
            return;

        var zoom = PrintPreviewViewer.Zoom;
        var horizontalOffset = previewScrollViewer?.HorizontalOffset ?? 0;
        var verticalOffset = previewScrollViewer?.VerticalOffset ?? 0;
        var revision = ++previewDocumentRevision;
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure(
            "preview.refresh",
            () => $"items={CurrentLabelPage.Items.Count};zoom={PrintPreviewViewer.Zoom:F1}");
#endif

        var layout = CurrentCatalog.Layout;
        var orientation = layout.Orientation == "Portrait" ? "Hochformat" : "Querformat";
        PreviewDescription.Text = $"{layout.PaperFormat} · {orientation} · {CurrentCatalog.Name} · {CurrentLabelPage.Name}";
        var selectedItem = (LabelGrid.SelectedItem as CatalogGridRow)?.Item;
        if (selectedItem is null || !CurrentLabelPage.Items.Contains(selectedItem))
            selectedItem = null;
        previewLabelCells.Clear();
        highlightedPreviewCell = null;
        PrintPreviewViewer.Document = PrintService.CreateLabelDocument(
            data,
            [CurrentLabelPage],
            photoLibrary,
            selectedItem,
            PreviewLabelItem_Clicked,
            previewLabelCells,
            layout,
            CurrentCatalog.Name);
        highlightedPreviewCell = previewLabelCells.FirstOrDefault(
            cell => ReferenceEquals(cell.Tag, selectedItem));

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (revision != previewDocumentRevision)
                return;

            PrintPreviewViewer.Zoom = zoom;
            previewScrollViewer?.ScrollToHorizontalOffset(horizontalOffset);
            previewScrollViewer?.ScrollToVerticalOffset(verticalOffset);
        });
    }

    private void UpdateLabelPreviewHighlight()
    {
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure(
            "preview.highlight",
            () => $"cells={previewLabelCells.Count}");
#endif
        if (previewLabelCells.Count == 0)
            return;

        var selectedItem = (LabelGrid.SelectedItem as CatalogGridRow)?.Item;
        if (selectedItem is not null && !CurrentLabelPage.Items.Contains(selectedItem))
            selectedItem = null;
        highlightedPreviewCell = PrintService.UpdateLabelPreviewHighlight(
            previewLabelCells, selectedItem, highlightedPreviewCell);
    }

    private void PrintPreviewViewer_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            PrintPreviewViewerConfiguration.RemoveUnusedToolbarControls(PrintPreviewViewer);
            previewScrollViewer = FindVisualChild<ScrollViewer>(PrintPreviewViewer);
            if (previewScrollViewer is null)
                return;

            previewScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            previewScrollViewer.PanningMode = PanningMode.Both;
        });
    }

    private void PrintPreviewViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || previewScrollViewer is null)
            return;

        var pointer = e.GetPosition(previewScrollViewer);
        var hit = previewScrollViewer.InputHitTest(pointer) as DependencyObject;
        var anchor = FindPreviewZoomAnchor(hit, previewScrollViewer);
        previewZoomAnchor = anchor;
        previewZoomAnchorPoint = anchor is null ? default : e.GetPosition(anchor);
        previewZoomViewportPoint = pointer;
        pendingPreviewZoomDelta += e.Delta;

        if (!previewZoomTimer.IsEnabled)
            previewZoomTimer.Start();

        e.Handled = true;
    }

    private void PreviewZoomTimer_Tick(object? sender, EventArgs e)
    {
        var delta = pendingPreviewZoomDelta;
        pendingPreviewZoomDelta = 0;
        if (delta == 0)
        {
            previewZoomTimer.Stop();
            return;
        }

        var newZoom = Math.Clamp(
            PrintPreviewViewer.Zoom * Math.Pow(1.0006, delta),
            25,
            400);
        if (Math.Abs(newZoom - PrintPreviewViewer.Zoom) < 0.01)
            return;

#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure(
            "preview.zoom",
            () => $"delta={delta};from={PrintPreviewViewer.Zoom:F1};to={newZoom:F1}");
#endif
        PrintPreviewViewer.Zoom = newZoom;
        if (previewZoomRestorePending)
            return;

        previewZoomRestorePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            previewZoomRestorePending = false;
            RestorePreviewZoomAnchor();
        });
    }

    private void RestorePreviewZoomAnchor()
    {
        if (previewScrollViewer is null ||
            previewZoomAnchor is null ||
            !previewZoomAnchor.IsVisible ||
            PresentationSource.FromVisual(previewZoomAnchor) != PresentationSource.FromVisual(previewScrollViewer))
            return;

        var currentAnchorPoint = previewZoomAnchor.TranslatePoint(previewZoomAnchorPoint, previewScrollViewer);
        previewScrollViewer.ScrollToHorizontalOffset(
            previewScrollViewer.HorizontalOffset + currentAnchorPoint.X - previewZoomViewportPoint.X);
        previewScrollViewer.ScrollToVerticalOffset(
            previewScrollViewer.VerticalOffset + currentAnchorPoint.Y - previewZoomViewportPoint.Y);
    }

    private static UIElement? FindPreviewZoomAnchor(DependencyObject? element, ScrollViewer scrollViewer)
    {
        UIElement? nearest = null;
        while (element is not null && !ReferenceEquals(element, scrollViewer))
        {
            if (element is UIElement visual)
            {
                nearest ??= visual;
                var typeName = visual.GetType().Name;
                if (typeName is "DocumentPageView" or "FixedPage")
                    return visual;
            }

            element = element switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(element),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return nearest;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        windowSource = (HwndSource?)PresentationSource.FromVisual(this);
        windowSource?.AddHook(MainWindow_WindowMessage);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        updateCheckCancellation.Cancel();
        updateCheckCancellation.Dispose();
        previewZoomTimer.Stop();
        AppDiagnostics.Event("mainWindow.closed");
        AppDiagnostics.StopUiThreadMonitor();
        windowSource?.RemoveHook(MainWindow_WindowMessage);
        windowSource = null;
    }

    private IntPtr MainWindow_WindowMessage(
        IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmMouseHWheel = 0x020E;
        if (message != wmMouseHWheel || previewScrollViewer is null)
            return IntPtr.Zero;

        var screenPoint = new Point(
            (short)(lParam.ToInt64() & 0xFFFF),
            (short)((lParam.ToInt64() >> 16) & 0xFFFF));
        var viewerOrigin = PrintPreviewViewer.PointToScreen(new Point());
        var viewerBounds = new Rect(viewerOrigin, PrintPreviewViewer.RenderSize);
        if (!viewerBounds.Contains(screenPoint))
            return IntPtr.Zero;

        var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        ScrollViewerWheelBehavior.ScrollBy(previewScrollViewer, horizontal: true, delta * 0.5);
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

    private static string NonEmptyName(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public sealed class CatalogGridRow : INotifyPropertyChanged
{
    private bool isHighlighted;

    public CatalogGridRow(
        CatalogPage page,
        CatalogItem item,
        int itemIndex,
        int index,
        int pageIndex,
        Dictionary<string, string?> photoPaths)
    {
        Page = page;
        Item = item;
        ItemIndex = itemIndex;
        PageIndex = pageIndex;
        PhotoPaths = photoPaths;
        Index = index;
    }

    public CatalogPage Page { get; }
    public CatalogItem Item { get; }
    public int ItemIndex { get; }
    public int PageIndex { get; }
    public Dictionary<string, string?> PhotoPaths { get; }
    public Dictionary<string, string> FieldValues => Item.FieldValues;
    public string? PhotoPath => PhotoPaths.Values.FirstOrDefault();
    public int Index { get; }
    public string PositionText => IsDropPlaceholder ? "" : Index.ToString(CultureInfo.CurrentCulture);
    public bool IsDropPlaceholder { get; init; }
    public int DropIndex { get; init; } = -1;
    public string Title { get => Item.Title; set => Item.Title = value ?? ""; }
    public string Beco { get => Item.Beco; set => Item.Beco = value ?? ""; }
    public string Ref { get => Item.Ref; set => Item.Ref = value ?? ""; }
    public string Price
    {
        get => Item.Price;
        set => Item.Price = value ?? "";
    }
    public string Ek { get => Item.Ek; set => Item.Ek = value ?? ""; }
    public string SalesPrice { get => Item.SalesPrice; set => Item.SalesPrice = value ?? ""; }

    public bool IsHighlighted
    {
        get => isHighlighted;
        set
        {
            if (isHighlighted == value)
                return;
            isHighlighted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHighlighted)));
        }
    }
    public bool IsDragTarget
    {
        get => isDragTarget;
        set
        {
            if (isDragTarget == value)
                return;
            isDragTarget = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDragTarget)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool isDragTarget;
}

internal sealed class DragPreviewAdorner : Adorner
{
    private readonly BitmapSource image;
    private readonly double scale;
    private Point pointerPosition;

    public DragPreviewAdorner(UIElement adornedElement, BitmapSource image) : base(adornedElement)
    {
        this.image = image;
        scale = Math.Min(1, 520 / image.PixelWidth);
        IsHitTestVisible = false;
    }

    public void SetPosition(Point position)
    {
        pointerPosition = position;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;
        var left = Math.Clamp(pointerPosition.X + 14, 0, Math.Max(0, AdornedElement.RenderSize.Width - width));
        var top = Math.Clamp(pointerPosition.Y + 12, 0, Math.Max(0, AdornedElement.RenderSize.Height - height));
        var bounds = new Rect(left, top, width, height);

        drawingContext.PushOpacity(0.82);
        drawingContext.DrawImage(image, bounds);
        drawingContext.DrawRoundedRectangle(
            null,
            new Pen(new SolidColorBrush(Color.FromRgb(36, 74, 115)), 1.5),
            bounds,
            4,
            4);
        drawingContext.Pop();
    }
}

public sealed record SearchResultRow(
    EditorCatalog Catalog,
    int CatalogIndex,
    CatalogPage Page,
    int PageIndex,
    CatalogItem Item,
    int ItemIndex,
    string DisplayText);
internal sealed record LabelSearchResult(CatalogPage Page, int PageIndex, CatalogItem Item, int ItemIndex);

public sealed class TextPromptWindow : Window
{
    private readonly TextBox input;

    private TextPromptWindow(string title, string prompt, string initialValue)
    {
        Title = title;
        Width = 430;
        MinHeight = 170;
        MaxHeight = Math.Max(220, SystemParameters.WorkArea.Height * 0.8);
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        input = new TextBox { Text = initialValue };
        panel.Children.Add(input);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        buttons.Children.Add(new Button { Content = "Abbrechen", IsCancel = true });
        var ok = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0) };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }

    public static string? Show(Window owner, string title, string prompt, string initialValue)
    {
        var dialog = new TextPromptWindow(title, prompt, initialValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.input.Text : null;
    }
}

public sealed class ConfirmationPromptWindow : Window
{
    private ConfirmationPromptWindow(string title, string message, string confirmLabel)
    {
        Title = title;
        Width = 460;
        MinHeight = 160;
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height * 0.8);
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = System.Windows.Media.Brushes.White;

        var panel = new DockPanel { Margin = new Thickness(20) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, IsDefault = true };
        var confirm = new Button
        {
            Content = confirmLabel,
            MinWidth = 92,
            Margin = new Thickness(0)
        };
        confirm.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(140, SystemParameters.WorkArea.Height * 0.58),
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        Content = panel;
        Loaded += (_, _) => cancel.Focus();
    }

    public static bool Show(Window owner, string title, string message, string confirmLabel)
    {
        var dialog = new ConfirmationPromptWindow(title, message, confirmLabel) { Owner = owner };
        return dialog.ShowDialog() == true;
    }
}