using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private CatalogData data;
    private int furniturePageIndex;
    private int employeePageIndex;
    private bool isUpdatingPageSelectors;
    private bool isSelectingSearchResult;
    private ScrollViewer? previewScrollViewer;
    private readonly List<Border> previewFurnitureCells = [];
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

    public List<string> FurniturePageNames => data.Pages.Select(page => page.Name).ToList();
    public List<string> EmployeePageNames => data.EmployeePages.Select(page => page.Name).ToList();

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
                PromptInitialCatalogSetup();
            await CheckForAvailableUpdateOnStartupAsync();
        };
        Closing += (_, _) =>
        {
            FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            EmployeeGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            Save();
        };
    }

    private CatalogPage CurrentFurniturePage => data.Pages[furniturePageIndex];
    private EmployeePage CurrentEmployeePage => data.EmployeePages[employeePageIndex];

    private void RefreshPages()
    {
        isUpdatingPageSelectors = true;
        FurniturePageBox.ItemsSource = FurniturePageNames;
        EmployeePageBox.ItemsSource = EmployeePageNames;
        FurniturePageBox.SelectedIndex = furniturePageIndex;
        EmployeePageBox.SelectedIndex = employeePageIndex;
        isUpdatingPageSelectors = false;
        RefreshFurnitureRows();
        RefreshEmployeeRows();
        RefreshPrintPreview();
        RefreshSearchResults();
    }

    private void RefreshFurnitureRows()
    {
        if (FurnitureGrid is null || data.Pages.Count == 0)
            return;
        var query = SearchBox.Text.Trim();
        FurnitureGrid.ItemsSource = CurrentFurniturePage.Items
            .Select((item, index) => new CatalogGridRow(CurrentFurniturePage, item, index, index + 1, furniturePageIndex, photoLibrary.GetPhotoPath(data, item.Photo)))
            .Where(row => Matches(query, CurrentFurniturePage.Name, row.Title, row.Beco, row.Ref,
                row.Price, row.Ek, row.SalesPrice, row.RecommendedPrice))
            .ToList();
        FurnitureCountText.Text = $"  15 Etiketten · {CurrentFurniturePage.Items.Count(item => !string.IsNullOrWhiteSpace(item.Title) || !string.IsNullOrWhiteSpace(item.Beco) || !string.IsNullOrWhiteSpace(item.Ref))} gepflegt";
    }

    private void RefreshEmployeeRows()
    {
        if (EmployeeGrid is null || data.EmployeePages.Count == 0)
            return;
        var query = SearchBox.Text.Trim();
        EmployeeGrid.ItemsSource = CurrentEmployeePage.Employees
            .Where(employee => Matches(query, CurrentEmployeePage.Name, employee.Name, employee.Number, employee.Code))
            .ToList();
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

    private void EmployeePageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingPageSelectors || EmployeePageBox.SelectedIndex < 0)
            return;
        employeePageIndex = EmployeePageBox.SelectedIndex;
        RefreshEmployeeRows();
        RefreshPrintPreview();
    }

    private void EditorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, EditorTabs))
            return;
        if (isSelectingSearchResult)
            return;
        RefreshFurnitureRows();
        RefreshEmployeeRows();
        RefreshPrintPreview();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshFurnitureRows();
        RefreshEmployeeRows();
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

        var results = data.Pages
            .SelectMany((page, pageIndex) => page.Items.Select((item, itemIndex) =>
                new FurnitureSearchResult(page, pageIndex, item, itemIndex)))
            .Where(result => Matches(query, result.Page.Name, result.Item.Title, result.Item.Beco,
                result.Item.Ref, result.Item.Price, result.Item.Ek, result.Item.SalesPrice,
                result.Item.RecommendedPrice))
            .Select(result => new SearchResultRow(result.Page, result.PageIndex, result.Item, result.ItemIndex,
                $"{result.Page.Name} · Etikett {result.ItemIndex + 1} · {SearchSummary(result.Item)}"))
            .ToList();
        SearchResultsBox.ItemsSource = results;
        SearchResultsBox.Visibility = results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string SearchSummary(CatalogItem item)
    {
        var summary = string.Join(" · ", new[] { item.Title, item.Beco, item.Ref }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return summary.Length == 0 ? "Leeres Etikett" : summary;
    }

    private void SearchResultsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isSelectingSearchResult || SearchResultsBox.SelectedItem is not SearchResultRow result)
            return;

        isSelectingSearchResult = true;
        var pageChanged = EditorTabs.SelectedIndex != 0 || furniturePageIndex != result.PageIndex;
        EditorTabs.SelectedIndex = 0;
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

    private void CodeTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo ||
            combo.DataContext is not CatalogGridRow row ||
            !row.CanChooseBarcode ||
            (!combo.IsKeyboardFocusWithin && !combo.IsMouseOver))
            return;

        Dispatcher.BeginInvoke(() =>
        {
            Save();
            RefreshPrintPreview();
        }, DispatcherPriority.Background);
    }

    private void PreviewFurnitureItem_Clicked(CatalogItem item)
    {
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure(
            "preview.cell.click",
            () => $"position={CurrentFurniturePage.Items.IndexOf(item) + 1}");
#endif
        EditorTabs.SelectedIndex = 0;
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
        EditFurniturePhoto(row.Page, row.ItemIndex);
    }

    private void EditFurniturePhoto(CatalogPage page, int itemIndex)
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

        page.Items[itemIndex].Photo = picker.SelectedPhotoKey;
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
            MessageBox.Show("Wählen Sie zuerst ein Etikett aus.", "Etikett verschieben",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var target = TextPromptWindow.Show(this, "Etikett verschieben",
            "Neue Position (1–15)", (row.ItemIndex + 1).ToString());
        if (target is null)
            return;
        if (!int.TryParse(target, out var targetPosition) || targetPosition is < 1 or > 15)
        {
            MessageBox.Show("Geben Sie eine Position von 1 bis 15 ein.", "Etikett verschieben",
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
            MessageBox.Show("Wählen Sie zuerst ein Etikett aus.", "Etikett verschieben",
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
        var editor = new FurnitureLayoutWindow(data, sample, photoLibrary, data.FurniturePrintLayout) { Owner = this };
        if (editor.ShowDialog() != true)
            return;

        data.FurniturePrintLayout.CopyFrom(editor.Result);
        Save();
        RefreshPrintPreview();
    }

    private void FurnitureGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            Save();
            RefreshPrintPreview();
        }, DispatcherPriority.Background);

    private void QuickServiceCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox ||
            !checkBox.IsLoaded ||
            checkBox.DataContext is not CatalogGridRow row)
            return;

        row.QuickService = checkBox.IsChecked == true;
        Save();
        RefreshPrintPreview();
    }

    private void EmployeeGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            Save();
            RefreshPrintPreview();
        }, DispatcherPriority.Background);

    private void Save()
    {
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure("catalog.save");
#endif
        try
        {
            store.SaveCatalog(data);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Exception("catalog.save", ex);
            MessageBox.Show($"Die Änderungen konnten nicht gespeichert werden.\n\n{ex.Message}",
                "Speicherfehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddFurniturePage_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Show(this, "Neue Kategorie", "Name der neuen Kategorie", "Neue Kategorie");
        if (name is null)
            return;
        data.Pages.Add(CatalogPage.Create(NonEmptyName(name, "Neue Kategorie")));
        furniturePageIndex = data.Pages.Count - 1;
        RefreshPages();
        Save();
    }

    private void RenameFurniturePage_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Show(this, "Kategorie umbenennen", "Neuer Kategoriename", CurrentFurniturePage.Name);
        if (name is null)
            return;
        CurrentFurniturePage.Name = NonEmptyName(name, CurrentFurniturePage.Name);
        RefreshPages();
        Save();
    }

    private void DeleteFurniturePage_Click(object sender, RoutedEventArgs e)
    {
        var replacesLastPage = data.Pages.Count == 1;
        var replacementMessage = replacesLastPage
            ? "\nEine neue leere Kategorie wird als Ersatz angelegt."
            : "";
        if (MessageBox.Show(
                $"Kategorie „{CurrentFurniturePage.Name}“ einschließlich Etiketten löschen?{replacementMessage}",
                "Kategorie löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (replacesLastPage)
        {
            data.Pages[furniturePageIndex] = CatalogPage.Create(CreateUnusedFurniturePageName());
        }
        else
        {
            data.Pages.RemoveAt(furniturePageIndex);
            furniturePageIndex = Math.Min(furniturePageIndex, data.Pages.Count - 1);
        }
        RefreshPages();
        Save();
    }

    private string CreateUnusedFurniturePageName()
    {
        const string baseName = "Neue Kategorie";
        var existingNames = data.Pages
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

    private void ResetItem_Click(object sender, RoutedEventArgs e)
    {
        if (FurnitureGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wählen Sie zuerst ein Etikett aus.", "Etikett zurücksetzen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Etikett {row.Index} zurücksetzen?", "Etikett zurücksetzen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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

        if (MessageBox.Show(
                $"Artikel an Position {itemIndex + 1} löschen? Die folgenden Artikel rücken auf. Am Ende wird ein leerer Platz ergänzt.",
                "Artikel löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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

    private void AddEmployeePage_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Show(this, "Neue Filiale", "Name der neuen Filiale", "Neue Filiale");
        if (name is null)
            return;
        data.EmployeePages.Add(new EmployeePage { Name = NonEmptyName(name, "Neue Filiale") });
        employeePageIndex = data.EmployeePages.Count - 1;
        RefreshPages();
        Save();
    }

    private void RenameEmployeePage_Click(object sender, RoutedEventArgs e)
    {
        var name = TextPromptWindow.Show(this, "Filiale umbenennen", "Neuer Filialname", CurrentEmployeePage.Name);
        if (name is null)
            return;
        CurrentEmployeePage.Name = NonEmptyName(name, CurrentEmployeePage.Name);
        RefreshPages();
        Save();
    }

    private void DeleteEmployeePage_Click(object sender, RoutedEventArgs e)
    {
        if (data.EmployeePages.Count == 1)
        {
            MessageBox.Show("Mindestens eine Filiale muss bestehen bleiben.", "Filiale löschen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Filiale „{CurrentEmployeePage.Name}“ einschließlich Mitarbeitenden löschen?",
                "Filiale löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        data.EmployeePages.RemoveAt(employeePageIndex);
        employeePageIndex = Math.Min(employeePageIndex, data.EmployeePages.Count - 1);
        RefreshPages();
        Save();
    }

    private void AddEmployee_Click(object sender, RoutedEventArgs e)
    {
        CurrentEmployeePage.Employees.Add(new Employee());
        RefreshEmployeeRows();
        EmployeeGrid.SelectedIndex = EmployeeGrid.Items.Count - 1;
        RefreshPrintPreview();
        Save();
    }

    private void DeleteEmployee_Click(object sender, RoutedEventArgs e)
    {
        if (EmployeeGrid.SelectedItem is not Employee employee)
        {
            MessageBox.Show("Wählen Sie zuerst einen Mitarbeitenden aus.", "Mitarbeiter löschen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Mitarbeiter „{employee.Name}“ löschen?", "Mitarbeiter löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        CurrentEmployeePage.Employees.Remove(employee);
        RefreshEmployeeRows();
        RefreshPrintPreview();
        Save();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Furnituren-Katalog importieren",
            Filter = "JSON-Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (MessageBox.Show("Der aktuelle Katalog wird ersetzt. Fortfahren?", "Katalog importieren",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        TryImportCatalog(dialog.FileName);
    }

    private void PromptInitialCatalogSetup()
    {
        var choice = MessageBox.Show(
            "Möchtest du einen vorhandenen JSON-Katalog importieren?\n\n" +
            "Ja: JSON-Datei auswählen und übernehmen.\n" +
            "Nein: Mit einem neuen, leeren Katalog beginnen.",
            "Katalog einrichten",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Furnituren-Katalog importieren",
            Filter = "JSON-Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
            TryImportCatalog(dialog.FileName);
    }

    private void TryImportCatalog(string fileName)
    {
        try
        {
            var imported = JsonSerializer.Deserialize<CatalogData>(File.ReadAllText(fileName), JsonOptions)
                ?? throw new InvalidDataException("Die Datei enthält keinen gültigen Katalog.");
            if (imported.Pages is null || imported.Pages.Count == 0)
                throw new InvalidDataException("Die Datei enthält keine Furnituren-Kategorien.");
            if (imported.AdditionalData?.TryGetValue("employees", out var legacyEmployees) == true &&
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
                imported.AdditionalData.Remove("employees");
            }
            imported.Normalize();
            var skippedPhotos = photoLibrary.NormalizeCatalogForImport(
                imported, Path.GetDirectoryName(fileName));
            data = imported;
            furniturePageIndex = 0;
            employeePageIndex = 0;
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
            Title = "Furnituren-Katalog exportieren",
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
                "Furnituren Editor – Update verfügbar",
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
        EmployeeGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        if (EditorTabs.SelectedIndex == 0)
            PrintService.PrintFurniture(this, data, [CurrentFurniturePage], photoLibrary);
        else
            PrintService.PrintEmployees(this, [CurrentEmployeePage], data.EmployeeLayout);
    }

    private void PrintAll_Click(object sender, RoutedEventArgs e)
    {
        FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        EmployeeGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        if (EditorTabs.SelectedIndex == 0)
            PrintService.PrintFurniture(this, data, data.Pages, photoLibrary);
        else
            PrintService.PrintEmployees(this, data.EmployeePages, data.EmployeeLayout);
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
        EmployeeGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        if (EditorTabs.SelectedIndex == 0)
            PrintService.SaveFurniturePdf(this, data, [CurrentFurniturePage], photoLibrary, CurrentFurniturePage.Name);
        else
            PrintService.SaveEmployeesPdf(this, [CurrentEmployeePage], data.EmployeeLayout, CurrentEmployeePage.Name);
    }

    private void SaveAllAsPdf_Click(object sender, RoutedEventArgs e)
    {
        FurnitureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        EmployeeGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        Save();
        if (EditorTabs.SelectedIndex == 0)
            PrintService.SaveFurniturePdf(this, data, data.Pages, photoLibrary, "alle-kategorien");
        else
            PrintService.SaveEmployeesPdf(this, data.EmployeePages, data.EmployeeLayout, "alle-filialen");
    }

    private void QuickServiceQr_Click(object sender, RoutedEventArgs e) =>
        PrintService.ExportQuickServiceQrZip(this, data.Pages);

    private void ChoosePhoto_Click(object sender, RoutedEventArgs e)
    {
        if (FurnitureGrid.SelectedItem is not CatalogGridRow row)
        {
            MessageBox.Show("Wähle zuerst ein Etikett in der Liste aus.", "Foto auswählen",
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

        CurrentFurniturePage.Items[row.Index - 1].Photo = picker.SelectedPhotoKey;
        Save();
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
        if (PrintPreviewViewer is null || data.Pages.Count == 0 || data.EmployeePages.Count == 0)
            return;

        var zoom = PrintPreviewViewer.Zoom;
        var horizontalOffset = previewScrollViewer?.HorizontalOffset ?? 0;
        var verticalOffset = previewScrollViewer?.VerticalOffset ?? 0;
        var revision = ++previewDocumentRevision;
#if INTERNAL_DIAGNOSTICS
        using var diagnostic = AppDiagnostics.Measure(
            "preview.refresh",
            () => $"tab={EditorTabs.SelectedIndex};items={CurrentFurniturePage.Items.Count};zoom={PrintPreviewViewer.Zoom:F1}");
#endif

        if (EditorTabs.SelectedIndex == 0)
        {
            PreviewDescription.Text = $"A4 quer · Etiketten · {CurrentFurniturePage.Name}";
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
                previewFurnitureCells);
            highlightedPreviewCell = previewFurnitureCells.FirstOrDefault(
                cell => ReferenceEquals(cell.Tag, selectedItem));
        }
        else
        {
            PreviewDescription.Text = $"A4 hoch · Mitarbeitendencodes · {CurrentEmployeePage.Name}";
            previewFurnitureCells.Clear();
            highlightedPreviewCell = null;
            PrintPreviewViewer.Document = PrintService.CreateEmployeeDocument([CurrentEmployeePage], data.EmployeeLayout);
        }

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
        if (EditorTabs.SelectedIndex != 0 || previewFurnitureCells.Count == 0)
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

    public CatalogGridRow(CatalogPage page, CatalogItem item, int itemIndex, int index, int pageIndex, string? photoPath)
    {
        Page = page;
        Item = item;
        ItemIndex = itemIndex;
        PageIndex = pageIndex;
        PhotoPath = photoPath;
        Index = index;
    }

    public CatalogPage Page { get; }
    public CatalogItem Item { get; }
    public int ItemIndex { get; }
    public int PageIndex { get; }
    public string? PhotoPath { get; }
    public int Index { get; }
    public string Title { get => Item.Title; set => Item.Title = value ?? ""; }
    public string Beco { get => Item.Beco; set => Item.Beco = value ?? ""; }
    public string Ref { get => Item.Ref; set => Item.Ref = value ?? ""; }
    public string Price
    {
        get => Item.Price;
        set => Item.Price = value ?? "";
    }
    public string Ek
    {
        get => Item.Ek;
        set
        {
            Item.Ek = value ?? "";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecommendedPrice)));
        }
    }
    public string SalesPrice { get => Item.SalesPrice; set => Item.SalesPrice = value ?? ""; }
    public string RecommendedPrice
    {
        get => !string.IsNullOrWhiteSpace(Item.RecommendedPrice)
            ? Item.RecommendedPrice
            : TryReadPrice(Item.Ek, out var cost)
                ? RoundRecommendedPrice(cost * 2m).ToString("0.00", CultureInfo.CurrentCulture)
                : "";
        set
        {
            Item.RecommendedPrice = value ?? "";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecommendedPrice)));
        }
    }

    private static decimal RoundRecommendedPrice(decimal price) => Math.Floor(price * 10m) / 10m;

    public IReadOnlyList<string> CodeTypeOptions { get; } = ["QR-Code", "Barcode"];
    public bool CanChooseBarcode => !Item.QuickService;
    public string CodeType
    {
        get => Item.UseBarcode ? "Barcode" : "QR-Code";
        set
        {
            Item.UseBarcode = !Item.QuickService && string.Equals(value, "Barcode", StringComparison.Ordinal);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CodeType)));
        }
    }
    public bool QuickService
    {
        get => Item.QuickService;
        set
        {
            Item.QuickService = value;
            if (value)
                Item.UseBarcode = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanChooseBarcode)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CodeType)));
        }
    }
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

    private static bool TryReadPrice(string value, out decimal price) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out price) ||
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out price);

    private bool isDragTarget;
}

public sealed record SearchResultRow(CatalogPage Page, int PageIndex, CatalogItem Item, int ItemIndex, string DisplayText);
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