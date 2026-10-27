using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FuniturenEditor.Desktop;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalStore store;
    private readonly PhotoLibrary photoLibrary = new();
    private readonly bool isFirstRun;
    private bool setupCompleted;
    private CatalogData data;
    private int catalogIndex;
    private int furniturePageIndex;
    private bool isUpdatingPageSelectors;
    private bool isSelectingSearchResult;
    private ScrollViewer? previewScrollViewer;
    private readonly List<Border> previewFurnitureCells = [];
    private readonly List<DataGridColumn> furnitureFieldColumns = [];
    private Border? highlightedPreviewCell;
    private readonly DispatcherTimer previewZoomTimer;
    private int previewDocumentRevision;
    private HwndSource? windowSource;
    private UIElement? previewZoomAnchor;
    private Point previewZoomAnchorPoint;
    private Point previewZoomViewportPoint;
    private double pendingPreviewZoomDelta;
    private bool previewZoomRestorePending;
    private CatalogGridRow? furnitureDragSource;
    private CatalogGridRow? furnitureDragTarget;
    private Point furnitureDragStartPoint;
    private readonly DispatcherTimer highlightTimer;
    private readonly CancellationTokenSource updateCheckCancellation = new();

    public List<string> FurniturePageNames => CurrentCatalog.Lists.Select(page => page.Name).ToList();
    public List<string> CatalogNames => data.Catalogs.Select(catalog => catalog.Name).ToList();

    public MainWindow(LocalStore store, bool isFirstRun = false)
    {
        InitializeComponent();
        AppDiagnostics.StartUiThreadMonitor(Dispatcher);
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
        previewZoomTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        previewZoomTimer.Tick += PreviewZoomTimer_Tick;
        highlightTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        highlightTimer.Tick += (_, _) =>
        {
            highlightTimer.Stop();
            foreach (var row in FurnitureGrid.Items.OfType<CatalogGridRow>())
                row.IsHighlighted = false;
        };
        this.store = store;
        this.isFirstRun = isFirstRun;
        setupCompleted = !isFirstRun;
        if (isFirstRun)
        {
            Opacity = 0;
            ShowInTaskbar = false;
        }
        data = store.LoadCatalog();
        if (photoLibrary.NormalizeCatalog(data))
            store.SaveCatalog(data);
        DataContext = this;
        AppVersionText.Text = $"Version {GetType().Assembly.GetName().Version?.ToString(3) ?? "unbekannt"}";
        DatabasePathText.Text = store.DatabasePath;
        DatabasePathText.ToolTip = store.DatabasePath;
        RefreshPages();
        RefreshPrintPreview();
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
            FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            if (setupCompleted)
                Save();
        };
    }

    private EditorCatalog CurrentCatalog => data.Catalogs[catalogIndex];
    private CatalogPage CurrentFurniturePage => CurrentCatalog.Lists[furniturePageIndex];

    private void RefreshPages()
    {
        catalogIndex = Math.Clamp(catalogIndex, 0, data.Catalogs.Count - 1);
        furniturePageIndex = Math.Clamp(furniturePageIndex, 0, CurrentCatalog.Lists.Count - 1);
        isUpdatingPageSelectors = true;
        CatalogBox.ItemsSource = CatalogNames;
        CatalogBox.SelectedIndex = catalogIndex;
        FurniturePageBox.ItemsSource = CurrentCatalog.Lists.Select(page => page.Name).ToList();
        FurniturePageBox.SelectedIndex = furniturePageIndex;
        isUpdatingPageSelectors = false;
        CatalogHeading.Text = CurrentCatalog.Name;
        CatalogDescription.Text = $"{CurrentCatalog.Lists.Count} Listen · {CurrentCatalog.Layout.Fields.Count} Vorlagenfelder";
        RefreshFurnitureRows();
        RefreshPrintPreview();
        RefreshSearchResults();
    }

    private void RefreshFurnitureRows()
    {
        if (FurnitureGrid is null || data.Catalogs.Count == 0 || CurrentCatalog.Lists.Count == 0)
            return;
        data.Normalize();
        RefreshFurnitureFieldColumns();
        var query = SearchBox.Text.Trim();
        FurnitureGrid.ItemsSource = CurrentFurniturePage.Items
            .Select((item, index) => new CatalogGridRow(
                CurrentFurniturePage,
                item,
                index,
                index + 1,
                furniturePageIndex,
                CurrentCatalog.Layout.Fields
                    .Where(field => field.Kind == "Photo")
                    .ToDictionary(field => field.Id, field => photoLibrary.GetPhotoPath(
                        data, item.FieldValues.GetValueOrDefault(field.Id, item.Photo)))))
            .Where(row => Matches(query, CurrentFurniturePage.Name, row.Title, row.Beco, row.Ref,
                row.Price, row.Ek, row.SalesPrice, string.Join(" ", row.Item.FieldValues.Values)))
            .ToList();
        FurnitureCountText.Text = $"  {CurrentFurniturePage.Items.Count} Einträge in „{CurrentFurniturePage.Name}“";
    }

    private void RefreshFurnitureFieldColumns()
    {
        foreach (var column in furnitureFieldColumns)
            FurnitureGrid.Columns.Remove(column);
        furnitureFieldColumns.Clear();

        foreach (var field in CurrentCatalog.Layout.Fields)
        {
            DataGridColumn column;
            if (field.Kind == "Photo")
            {
                var button = new FrameworkElementFactory(typeof(Button));
                button.SetValue(Button.StyleProperty, FurnitureGrid.Resources["PhotoPickerButton"]);
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
                    Width = new DataGridLength(82),
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
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    ElementStyle = (Style)FurnitureGrid.Resources["CenteredCellText"],
                    EditingElementStyle = (Style)FurnitureGrid.Resources["CenteredCellEditor"]
                };
            }

            FurnitureGrid.Columns.Add(column);
            furnitureFieldColumns.Add(column);
        }
    }

    private static bool Matches(string search, params string?[] fields) =>
        search.Length == 0 || fields.Any(value => value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true);

    private void FurniturePageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingPageSelectors || FurniturePageBox.SelectedIndex < 0)
            return;
        furniturePageIndex = FurniturePageBox.SelectedIndex;
        RefreshFurnitureRows();
        RefreshPrintPreview();
    }

    private void CatalogBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingPageSelectors || CatalogBox.SelectedIndex < 0)
            return;
        catalogIndex = CatalogBox.SelectedIndex;
        furniturePageIndex = 0;
        RefreshPages();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshFurnitureRows();
        RefreshSearchResults();
    }

    private void SearchBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        RefreshSearchResults();
    }

    private void SearchBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RefreshSearchResults();
    }

    private void RefreshSearchResults()
    {
        if (SearchResultsBox is null)
            return;
        var query = SearchBox.Text.Trim();
        SearchResultsBox.ItemsSource = null;
        if (query.Length == 0)
        {
            SearchResultsBox.Visibility = Visibility.Collapsed;
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
        SearchResultsBox.Visibility = results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
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
        var pageChanged = catalogIndex != result.CatalogIndex || furniturePageIndex != result.PageIndex;
        catalogIndex = result.CatalogIndex;
        furniturePageIndex = result.PageIndex;
        if (pageChanged)
            RefreshPages();
        FurniturePageBox.SelectedIndex = furniturePageIndex;
        var row = FurnitureGrid.Items.OfType<CatalogGridRow>().FirstOrDefault(candidate => ReferenceEquals(candidate.Item, result.Item));
        if (row is not null)
        {
            FurnitureGrid.SelectedItem = row;
            FurnitureGrid.ScrollIntoView(row);
            HighlightFurnitureRow(result.Item);
        }
        SearchResultsBox.SelectedIndex = -1;
        SearchResultsBox.Visibility = Visibility.Collapsed;
        isSelectingSearchResult = false;
        UpdateFurniturePreviewHighlight();
    }

    private void FurnitureGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure(
            "grid.selection",
            () => $"added={e.AddedItems.Count};removed={e.RemovedItems.Count};previewCells={previewFurnitureCells.Count}");
#endif
        UpdateFurniturePreviewHighlight();
    }

    private void PreviewFurnitureItem_Clicked(CatalogItem item)
    {
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure(
            "preview.cell.click",
            () => $"position={CurrentFurniturePage.Items.IndexOf(item) + 1}");
#endif
        SearchBox.Clear();
        var row = FurnitureGrid.Items.OfType<CatalogGridRow>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Item, item));
        if (row is null)
            return;
        FurnitureGrid.SelectedItem = row;
        FurnitureGrid.ScrollIntoView(row);
        HighlightFurnitureRow(item);
        UpdateFurniturePreviewHighlight();
    }

    private void HighlightFurnitureRow(CatalogItem item)
    {
        highlightTimer.Stop();
        CatalogGridRow? target = null;
        foreach (var row in FurnitureGrid.Items.OfType<CatalogGridRow>())
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
        FurnitureGrid.SelectedItem = row;
        EditFurniturePhoto(row.Page, row.ItemIndex, (sender as FrameworkElement)?.Tag as string);
    }

    private void EditFurniturePhoto(CatalogPage page, int itemIndex, string? fieldId = null)
    {
        FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FurnitureGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var picker = new PhotoLibraryWindow(data, photoLibrary) { Owner = this };
        picker.Closed += (_, _) =>
        {
            if (!picker.HasChanges)
                return;
            Save();
            RefreshFurnitureRows();
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
        RefreshFurnitureRows();
        RefreshPrintPreview();
    }

    private void MoveFurnitureUp_Click(object sender, RoutedEventArgs e) => MoveSelectedFurniture(-1);

    private void MoveFurnitureDown_Click(object sender, RoutedEventArgs e) => MoveSelectedFurniture(1);

    private void MoveFurnitureTo_Click(object sender, RoutedEventArgs e)
    {
        if (FurnitureGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wählen Sie zuerst einen Eintrag aus.", "Eintrag verschieben",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var target = TextPromptWindow.Show(this, "Eintrag verschieben",
            $"Neue Position (1–{CurrentFurniturePage.Items.Count})", (row.ItemIndex + 1).ToString());
        if (target is null)
            return;
        if (!int.TryParse(target, out var targetPosition) || targetPosition is < 1 || targetPosition > CurrentFurniturePage.Items.Count)
        {
            MessageBox.Show($"Geben Sie eine Position von 1 bis {CurrentFurniturePage.Items.Count} ein.", "Eintrag verschieben",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MoveFurnitureTo(row, targetPosition - 1);
    }

    private void FurnitureGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var cell = FindVisualAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell?.Column.DisplayIndex != 0 || cell.DataContext is not CatalogGridRow row)
            return;

        furnitureDragSource = row;
        furnitureDragStartPoint = e.GetPosition(FurnitureGrid);
    }

    private void FurnitureGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (furnitureDragSource is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pointer = e.GetPosition(FurnitureGrid);
        if (Math.Abs(pointer.X - furnitureDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pointer.Y - furnitureDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var source = furnitureDragSource;
        FurnitureGrid.SelectedItem = source;
        FurnitureGrid.Cursor = Cursors.SizeAll;
        DragDrop.DoDragDrop(FurnitureGrid, source, DragDropEffects.Move);
        ResetFurnitureDrag();
        e.Handled = true;
    }

    private void FurnitureGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ResetFurnitureDrag();
    }

    private void FurnitureGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var dataGridRow = FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (dataGridRow?.Item is not CatalogGridRow row)
        {
            e.Handled = true;
            return;
        }

        FurnitureGrid.SelectedItem = row;
        var itemIndex = row.Page.Items.IndexOf(row.Item);
        MoveFurnitureUpMenuItem.IsEnabled = itemIndex > 0;
        MoveFurnitureDownMenuItem.IsEnabled = itemIndex >= 0 && itemIndex < row.Page.Items.Count - 1;
    }

    private void FurnitureGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(CatalogGridRow)) is not CatalogGridRow source)
            return;

        var targetRow = FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        var target = targetRow?.Item as CatalogGridRow;
        var targetIndex = targetRow is null
            ? -1
            : GetFurnitureDropIndex(source, targetRow, e.GetPosition(targetRow));
        var canDrop = targetIndex >= 0;

        SetFurnitureDragTarget(canDrop ? target : null);
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void FurnitureGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(CatalogGridRow)) is not CatalogGridRow source)
            return;

        var targetRow = FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        var targetIndex = targetRow is null
            ? -1
            : GetFurnitureDropIndex(source, targetRow, e.GetPosition(targetRow));

        ResetFurnitureDrag();
        if (targetIndex >= 0)
        {
            MoveFurnitureTo(source, targetIndex);
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private static int GetFurnitureDropIndex(CatalogGridRow source, DataGridRow targetRow, Point position)
    {
        if (targetRow.Item is not CatalogGridRow target ||
            !ReferenceEquals(target.Page, source.Page) ||
            ReferenceEquals(target.Item, source.Item))
            return -1;

        var sourceIndex = source.Page.Items.IndexOf(source.Item);
        var targetIndex = source.Page.Items.IndexOf(target.Item);
        if (sourceIndex < 0 || targetIndex < 0)
            return -1;

        var dropAfter = position.Y >= targetRow.ActualHeight / 2;
        var insertionIndex = sourceIndex < targetIndex
            ? targetIndex - (dropAfter ? 0 : 1)
            : targetIndex + (dropAfter ? 1 : 0);
        return Math.Clamp(insertionIndex, 0, source.Page.Items.Count - 1);
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

    private void SetFurnitureDragTarget(CatalogGridRow? target)
    {
        if (ReferenceEquals(furnitureDragTarget, target))
            return;

        if (furnitureDragTarget is not null)
            furnitureDragTarget.IsDragTarget = false;

        furnitureDragTarget = target;
        if (furnitureDragTarget is not null)
            furnitureDragTarget.IsDragTarget = true;
    }

    private void ResetFurnitureDrag()
    {
        SetFurnitureDragTarget(null);
        furnitureDragSource = null;
        FurnitureGrid.Cursor = null;
    }

    private void MoveSelectedFurniture(int direction)
    {
        if (FurnitureGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wählen Sie zuerst einen Eintrag aus.", "Eintrag verschieben",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var currentIndex = row.Page.Items.IndexOf(row.Item);
        MoveFurnitureTo(row, currentIndex + direction);
    }

    private void MoveFurnitureTo(CatalogGridRow row, int targetIndex)
    {
        var currentIndex = row.Page.Items.IndexOf(row.Item);
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= row.Page.Items.Count)
            return;
        if (targetIndex == currentIndex)
            return;

        FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FurnitureGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var item = row.Page.Items[currentIndex];
        row.Page.Items.RemoveAt(currentIndex);
        row.Page.Items.Insert(targetIndex, item);
        RefreshFurnitureRows();
        var movedRow = FurnitureGrid.Items.OfType<CatalogGridRow>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Item, row.Item));
        if (movedRow is not null)
        {
            FurnitureGrid.SelectedItem = movedRow;
            FurnitureGrid.ScrollIntoView(movedRow);
        }
        Save();
        RefreshPrintPreview();
    }

    private void EditFurnitureLayout_Click(object sender, RoutedEventArgs e)
    {
        var sample = FurnitureGrid.SelectedItem is CatalogGridRow selected ? selected.Item : CurrentFurniturePage.Items[0];
        var oldFieldIds = CurrentCatalog.Layout.Fields.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
        var editor = new FurnitureLayoutWindow(data, sample, photoLibrary, CurrentCatalog.Layout) { Owner = this };
        if (editor.ShowDialog() != true)
            return;

        CurrentCatalog.Layout.CopyFrom(editor.Result);
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
        RefreshFurnitureRows();
        RefreshPrintPreview();
    }

    private void FurnitureGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            Save();
            RefreshPrintPreview();
        }, DispatcherPriority.Background);

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

    private void AddFurniturePage_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Show(this, "Neue Liste", "Name der Liste", "Neue Liste");
        if (name is null)
            return;
        CurrentCatalog.Lists.Add(CreateNewList(NonEmptyName(name, "Neue Liste"), CurrentCatalog.Layout.TileCount));
        furniturePageIndex = CurrentCatalog.Lists.Count - 1;
        RefreshPages();
        Save();
    }

    private void RenameFurniturePage_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Show(this, "Liste umbenennen", "Neuer Listenname", CurrentFurniturePage.Name);
        if (name is null)
            return;
        CurrentFurniturePage.Name = NonEmptyName(name, CurrentFurniturePage.Name);
        RefreshPages();
        Save();
    }

    private void DeleteFurniturePage_Click(object sender, RoutedEventArgs e)
    {
        var replacesLastPage = CurrentCatalog.Lists.Count == 1;
        var replacementMessage = replacesLastPage ? "\nEine leere Liste wird als Ersatz angelegt." : "";
        if (!ConfirmationPromptWindow.Show(
                this,
                "Liste löschen",
                $"Liste „{CurrentFurniturePage.Name}“ einschließlich aller Einträge löschen?{replacementMessage}",
                "Löschen"))
            return;

        if (replacesLastPage)
        {
            CurrentCatalog.Lists[furniturePageIndex] = CreateNewList(CreateUnusedFurniturePageName(), CurrentCatalog.Layout.TileCount);
        }
        else
        {
            CurrentCatalog.Lists.RemoveAt(furniturePageIndex);
            furniturePageIndex = Math.Min(furniturePageIndex, CurrentCatalog.Lists.Count - 1);
        }
        RefreshPages();
        Save();
    }

    private string CreateUnusedFurniturePageName()
    {
        const string baseName = "Neue Liste";
        var existingNames = CurrentCatalog.Lists
            .Where((_, index) => index != furniturePageIndex)
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

        var layout = new FurniturePrintLayout();
        layout.Normalize();
        var catalog = new EditorCatalog
        {
            Name = NonEmptyName(name, "Neuer Katalog"),
            Layout = layout,
            Lists = [CreateNewList("Liste 1", layout.TileCount)]
        };
        data.Catalogs.Add(catalog);
        catalogIndex = data.Catalogs.Count - 1;
        furniturePageIndex = 0;
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
        furniturePageIndex = 0;
        RefreshPages();
        Save();
    }

    private void ResetItem_Click(object sender, RoutedEventArgs e)
    {
        if (FurnitureGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wählen Sie zuerst einen Eintrag aus.", "Eintrag zurücksetzen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ConfirmationPromptWindow.Show(
                this, "Eintrag zurücksetzen", $"Eintrag {row.Index} zurücksetzen?", "Zurücksetzen"))
            return;
        CurrentFurniturePage.Items[row.Index - 1] = new CatalogItem();
        RefreshFurnitureRows();
        RefreshPrintPreview();
        Save();
    }

    private void DeleteFurnitureItem_Click(object sender, RoutedEventArgs e)
    {
        if (FurnitureGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wählen Sie zuerst einen Artikel aus.", "Artikel löschen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FurnitureGrid.CommitEdit(DataGridEditingUnit.Row, true);
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
        RefreshFurnitureRows();

        var nextItem = row.Page.Items[Math.Min(itemIndex, row.Page.Items.Count - 1)];
        var nextRow = FurnitureGrid.Items.OfType<CatalogGridRow>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Item, nextItem));
        if (nextRow is not null)
        {
            FurnitureGrid.SelectedItem = nextRow;
            FurnitureGrid.ScrollIntoView(nextRow);
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
        furniturePageIndex = 0;
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
            furniturePageIndex = 0;
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
            FileName = "furnituren-daten.json",
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
        new UpdateWindow { Owner = this }.ShowDialog();
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
        FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        PrintService.PrintFurniture(this, data, [CurrentFurniturePage], photoLibrary, CurrentCatalog.Layout, CurrentCatalog.Name);
    }

    private void PrintAll_Click(object sender, RoutedEventArgs e)
    {
        FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        PrintService.PrintFurniture(this, data, CurrentCatalog.Lists, photoLibrary, CurrentCatalog.Layout, CurrentCatalog.Name);
    }

    private void ShowActionMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void SaveCurrentAsPdf_Click(object sender, RoutedEventArgs e)
    {
        FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        PrintService.SaveFurniturePdf(this, data, [CurrentFurniturePage], photoLibrary, CurrentFurniturePage.Name,
            CurrentCatalog.Layout, CurrentCatalog.Name);
    }

    private void SaveAllAsPdf_Click(object sender, RoutedEventArgs e)
    {
        FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        PrintService.SaveFurniturePdf(this, data, CurrentCatalog.Lists, photoLibrary,
            $"alle-listen-{CurrentCatalog.Name}", CurrentCatalog.Layout, CurrentCatalog.Name);
    }

    private void QrBarcodeZip_Click(object sender, RoutedEventArgs e) =>
        PrintService.ExportQrBarcodeZip(this, data, CurrentCatalog.Lists, CurrentCatalog.Layout, CurrentCatalog.Name);

    private void ChoosePhoto_Click(object sender, RoutedEventArgs e)
    {
        if (FurnitureGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wähle zuerst einen Eintrag in der Liste aus.", "Foto auswählen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new PhotoLibraryWindow(data, photoLibrary) { Owner = this };
        picker.Closed += (_, _) =>
        {
            if (!picker.HasChanges)
                return;
            Save();
            RefreshFurnitureRows();
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
        RefreshFurnitureRows();
        RefreshPrintPreview();
    }

    private void TogglePreview_Click(object sender, RoutedEventArgs e)
    {
        var showPreview = PreviewPanel.Visibility != Visibility.Visible;
        PreviewPanel.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitter.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        EditorColumn.Width = showPreview ? new GridLength(3, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        PreviewSplitterColumn.Width = showPreview ? new GridLength(6) : new GridLength(0);
        PreviewColumn.Width = showPreview ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        PreviewToggleButton.Content = showPreview ? "Vorschau ausblenden" : "Druckvorschau";
        if (showPreview)
            RefreshPrintPreview();
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
            () => $"items={CurrentFurniturePage.Items.Count};zoom={PrintPreviewViewer.Zoom:F1}");
#endif

        var layout = CurrentCatalog.Layout;
        var orientation = layout.Orientation == "Portrait" ? "Hochformat" : "Querformat";
        PreviewDescription.Text = $"{layout.PaperFormat} · {orientation} · {CurrentCatalog.Name} · {CurrentFurniturePage.Name}";
        var selectedItem = (FurnitureGrid.SelectedItem as CatalogGridRow)?.Item;
        if (selectedItem is null || !CurrentFurniturePage.Items.Contains(selectedItem))
            selectedItem = null;
        previewFurnitureCells.Clear();
        highlightedPreviewCell = null;
        PrintPreviewViewer.Document = PrintService.CreateFurnitureDocument(
            data,
            [CurrentFurniturePage],
            photoLibrary,
            selectedItem,
            PreviewFurnitureItem_Clicked,
            previewFurnitureCells,
            layout,
            CurrentCatalog.Name);
        highlightedPreviewCell = previewFurnitureCells.FirstOrDefault(
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

    private void UpdateFurniturePreviewHighlight()
    {
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure(
            "preview.highlight",
            () => $"cells={previewFurnitureCells.Count}");
#endif
        if (previewFurnitureCells.Count == 0)
            return;

        var selectedItem = (FurnitureGrid.SelectedItem as CatalogGridRow)?.Item;
        if (selectedItem is not null && !CurrentFurniturePage.Items.Contains(selectedItem))
            selectedItem = null;
        highlightedPreviewCell = PrintService.UpdateFurniturePreviewHighlight(
            previewFurnitureCells, selectedItem, highlightedPreviewCell);
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

public sealed record SearchResultRow(
    EditorCatalog Catalog,
    int CatalogIndex,
    CatalogPage Page,
    int PageIndex,
    CatalogItem Item,
    int ItemIndex,
    string DisplayText);
internal sealed record FurnitureSearchResult(CatalogPage Page, int PageIndex, CatalogItem Item, int ItemIndex);

public sealed class TextPromptWindow : Window
{
    private readonly TextBox input;

    private TextPromptWindow(string title, string prompt, string initialValue)
    {
        Title = title;
        Width = 430;
        Height = 190;
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
        MaxHeight = 360;
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
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
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