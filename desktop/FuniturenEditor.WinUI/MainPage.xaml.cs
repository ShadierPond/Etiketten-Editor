using System.Text.Json;
using FuniturenEditor.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using QRCoder;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FuniturenEditor_WinUI;

public sealed partial class MainPage : Page
{
    private readonly LocalStore _store = new();
    private readonly StoragePhotoLibrary _photoLibrary = new();
    private CatalogData _catalog = CatalogData.CreateDefault();
    private CatalogPage? _selectedCategory;
    private CatalogItem? _selectedItem;
    private EmployeePage? _selectedBranch;
    private Employee? _selectedEmployee;
    private string? _signedInUser;
    private bool _isRefreshing;
    private bool _isAdministrator;

    private static readonly string[] LayoutElementNames =
        ["Foto", "QR-Code", "Beschreibung", "BECO", "Referenz", "Verkaufspreis", "Einkaufspreis"];

    public MainPage()
    {
        _isRefreshing = true;
        InitializeComponent();
        _isRefreshing = false;
        SetAuthenticationMode();
    }

    private void SetAuthenticationMode()
    {
        AuthenticationView.Visibility = Visibility.Visible;
        ShellNavigation.Visibility = Visibility.Collapsed;
        var isFirstRun = !_store.HasAccounts;
        AuthenticationTitle.Text = isFirstRun ? "Willkommen beim Furnituren Editor" : "Furnituren Editor";
        AuthenticationSubtitle.Text = isFirstRun
            ? "Lege zuerst ein Administratorkonto an. Es wird lokal auf diesem Computer gespeichert."
            : "Melde dich an, um deinen lokalen Katalog zu öffnen.";
        AuthenticationButton.Content = isFirstRun ? "Administratorkonto anlegen" : "Anmelden";
        PasswordStrengthPanel.Visibility = isFirstRun ? Visibility.Visible : Visibility.Collapsed;
        PasswordConfirmLabel.Visibility = isFirstRun ? Visibility.Visible : Visibility.Collapsed;
        PasswordConfirmInput.Visibility = isFirstRun ? Visibility.Visible : Visibility.Collapsed;
        PasswordConfirmInput.Password = "";
        UpdatePasswordStrength();
        AuthenticationInfoBar.IsOpen = false;
        UsernameBox.Focus(FocusState.Programmatic);
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e) => UpdatePasswordStrength();

    private void UpdatePasswordStrength()
    {
        var password = PasswordInput.Password;
        var groups = 0;
        if (password.Any(char.IsLower)) groups++;
        if (password.Any(char.IsUpper)) groups++;
        if (password.Any(char.IsDigit)) groups++;
        if (password.Any(character => !char.IsLetterOrDigit(character))) groups++;

        var level = password.Length == 0 ? 0 :
            password.Length >= 12 && groups >= 3 ? 3 :
            password.Length >= 8 || groups >= 3 ? 2 : 1;
        var (label, color) = level switch
        {
            1 => ("Schwach", Windows.UI.Color.FromArgb(255, 210, 55, 50)),
            2 => ("Mittel", Windows.UI.Color.FromArgb(255, 220, 158, 25)),
            3 => ("Stark", Windows.UI.Color.FromArgb(255, 37, 145, 83)),
            _ => ("Kein Passwort", Windows.UI.Color.FromArgb(255, 106, 119, 133))
        };
        var activeBrush = new SolidColorBrush(color);
        var inactiveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 216, 224, 232));
        PasswordStrengthText.Text = label;
        PasswordStrengthText.Foreground = activeBrush;
        StrengthSegment1.Background = level >= 1 ? activeBrush : inactiveBrush;
        StrengthSegment2.Background = level >= 2 ? activeBrush : inactiveBrush;
        StrengthSegment3.Background = level >= 3 ? activeBrush : inactiveBrush;
    }

    private void Authentication_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            AuthenticationButton_Click(sender, e);
    }

    private void AuthenticationButton_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordInput.Password;
        if (string.IsNullOrWhiteSpace(username))
        {
            ShowAuthenticationError("Bitte einen Benutzernamen eingeben.");
            return;
        }

        try
        {
            if (!_store.HasAccounts)
            {
                if (password != PasswordConfirmInput.Password)
                {
                    ShowAuthenticationError("Die Passwörter stimmen nicht überein.");
                    return;
                }
                _store.CreateFirstAdministrator(username, password);
            }
            else if (!_store.TryLogin(username, password, out var error))
            {
                ShowAuthenticationError(error);
                return;
            }

            _signedInUser = username;
            _isAdministrator = _store.IsAdministrator(username);
            _catalog = _store.LoadCatalog();
            var normalized = _photoLibrary.NormalizeCatalog(_catalog);
            if (normalized)
                _store.SaveCatalog(_catalog);
            AuthenticationInfoBar.IsOpen = false;
            AuthenticationView.Visibility = Visibility.Collapsed;
            ShellNavigation.Visibility = Visibility.Visible;
            SignedInLabel.Text = $"Angemeldet als {username}";
            UsersNavigationItem.Visibility = _isAdministrator ? Visibility.Visible : Visibility.Collapsed;
            RefreshCategories();
            RefreshBranches();
            RefreshUsers();
            ShowSection("furniture");
            ShellNavigation.SelectedItem = ShellNavigation.MenuItems[0];
            StatusInfoBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ShowAuthenticationError(ex.Message);
        }
    }

    private void ShowAuthenticationError(string message)
    {
        AuthenticationInfoBar.Title = "Anmeldung nicht möglich";
        AuthenticationInfoBar.Message = message;
        AuthenticationInfoBar.Severity = InfoBarSeverity.Error;
        AuthenticationInfoBar.IsOpen = true;
    }

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string section)
            ShowSection(section);
    }

    private void ShowSection(string section)
    {
        FurnitureView.Visibility = section == "furniture" ? Visibility.Visible : Visibility.Collapsed;
        EmployeesView.Visibility = section == "employees" ? Visibility.Visible : Visibility.Collapsed;
        LayoutView.Visibility = section == "layout" ? Visibility.Visible : Visibility.Collapsed;
        UsersView.Visibility = section == "users" && _isAdministrator ? Visibility.Visible : Visibility.Collapsed;
        if (section == "layout")
            LoadLayoutControls();
    }

    private void RefreshCategories()
    {
        _isRefreshing = true;
        CategoryBox.ItemsSource = _catalog.Pages.Select(page => page.Name).ToList();
        if (_selectedCategory is null || !_catalog.Pages.Contains(_selectedCategory))
            _selectedCategory = _catalog.Pages[0];
        CategoryBox.SelectedIndex = _catalog.Pages.IndexOf(_selectedCategory);
        _isRefreshing = false;
        RefreshFurnitureList();
    }

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || CategoryBox.SelectedIndex < 0 || CategoryBox.SelectedIndex >= _catalog.Pages.Count)
            return;
        _selectedCategory = _catalog.Pages[CategoryBox.SelectedIndex];
        _selectedItem = null;
        RefreshFurnitureList();
        ClearFurnitureEditor();
    }

    private void RefreshFurnitureList(int selectedIndex = -1)
    {
        if (_selectedCategory is null)
            return;

        var search = SearchBox.Text.Trim();
        _isRefreshing = true;
        FurnitureList.ItemsSource = CreateFurnitureRows();
        FurnitureList.SelectedIndex = selectedIndex;
        FurnitureSubtitle.Text = $"{_catalog.Pages.Count} Kategorien · 15 Positionen · {_selectedCategory.Name}";
        _isRefreshing = false;
        if (!string.IsNullOrWhiteSpace(search))
            UpdateSearchSuggestions(search);
    }

    private static string ItemSummary(CatalogItem item)
    {
        var title = string.IsNullOrWhiteSpace(item.Title) ? "Leere Position" : item.Title.Trim();
        if (!string.IsNullOrWhiteSpace(item.Ref))
            title += $"   ·   {item.Ref.Trim()}";
        return title;
    }

    private void FurnitureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || _selectedCategory is null || FurnitureList.SelectedIndex < 0)
            return;
        var index = FurnitureList.SelectedIndex;
        if (index >= _selectedCategory.Items.Count)
            return;
        _selectedItem = _selectedCategory.Items[index];
        LoadFurnitureEditor(index);
    }

    private void LoadFurnitureEditor(int index)
    {
        if (_selectedItem is null)
            return;
        _isRefreshing = true;
        ItemHeading.Text = $"Position {index + 1:00}";
        TitleBox.Text = _selectedItem.Title;
        BecoBox.Text = _selectedItem.Beco;
        ReferenceBox.Text = _selectedItem.Ref;
        PriceBox.Text = _selectedItem.Price;
        EkBox.Text = _selectedItem.Ek;
        ItemPositionBox.Value = index + 1;
        LoadPhotoPreview();
        ItemSaveStatus.Text = "Änderungen werden automatisch gespeichert.";
        _isRefreshing = false;
    }

    private void ClearFurnitureEditor()
    {
        _isRefreshing = true;
        _selectedItem = null;
        ItemHeading.Text = "Position auswählen";
        TitleBox.Text = "";
        BecoBox.Text = "";
        ReferenceBox.Text = "";
        PriceBox.Text = "";
        EkBox.Text = "";
        ItemPositionBox.Value = 1;
        PhotoPreview.Source = null;
        _isRefreshing = false;
    }

    private void FurnitureField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isRefreshing || _selectedItem is null)
            return;
        _selectedItem.Title = TitleBox.Text;
        _selectedItem.Beco = BecoBox.Text;
        _selectedItem.Ref = ReferenceBox.Text;
        _selectedItem.Price = PriceBox.Text;
        _selectedItem.Ek = EkBox.Text;
        SaveCatalog();
        var index = _selectedCategory?.Items.IndexOf(_selectedItem) ?? -1;
        if (index >= 0)
        {
            _isRefreshing = true;
            FurnitureList.ItemsSource = CreateFurnitureRows();
            FurnitureList.SelectedIndex = index;
            _isRefreshing = false;
        }
    }

    private List<FurnitureListEntry> CreateFurnitureRows() =>
        _selectedCategory!.Items.Select((item, index) =>
        {
            var path = _photoLibrary.GetPhotoPath(_catalog, item.Photo);
            return new FurnitureListEntry(
                $"{index + 1:00}     {ItemSummary(item)}",
                path is null ? null : new BitmapImage(new Uri(path)));
        }).ToList();

    private void ItemPosition_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isRefreshing || _selectedCategory is null || _selectedItem is null ||
            double.IsNaN(args.NewValue) || args.NewValue is < 1 or > 15)
            return;
        var currentIndex = _selectedCategory.Items.IndexOf(_selectedItem);
        var targetIndex = (int)Math.Round(args.NewValue) - 1;
        if (currentIndex < 0 || targetIndex == currentIndex)
            return;
        (_selectedCategory.Items[currentIndex], _selectedCategory.Items[targetIndex]) =
            (_selectedCategory.Items[targetIndex], _selectedCategory.Items[currentIndex]);
        RefreshFurnitureList(targetIndex);
        _selectedItem = _selectedCategory.Items[targetIndex];
        LoadFurnitureEditor(targetIndex);
        SaveCatalog();
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e) => MoveSelectedFurniture(-1);
    private void MoveDownButton_Click(object sender, RoutedEventArgs e) => MoveSelectedFurniture(1);

    private void MoveSelectedFurniture(int offset)
    {
        if (_selectedCategory is null || _selectedItem is null)
            return;
        var currentIndex = _selectedCategory.Items.IndexOf(_selectedItem);
        var targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= 15)
            return;
        (_selectedCategory.Items[currentIndex], _selectedCategory.Items[targetIndex]) =
            (_selectedCategory.Items[targetIndex], _selectedCategory.Items[currentIndex]);
        RefreshFurnitureList(targetIndex);
        _selectedItem = _selectedCategory.Items[targetIndex];
        LoadFurnitureEditor(targetIndex);
        SaveCatalog();
    }

    private void ResetItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory is null || _selectedItem is null)
            return;
        var index = _selectedCategory.Items.IndexOf(_selectedItem);
        _selectedCategory.Items[index] = new CatalogItem();
        _selectedItem = _selectedCategory.Items[index];
        RefreshFurnitureList(index);
        LoadFurnitureEditor(index);
        SaveCatalog();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            UpdateSearchSuggestions(sender.Text.Trim());
    }

    private void UpdateSearchSuggestions(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchBox.ItemsSource = Array.Empty<object>();
            return;
        }

        var categoryHits = _catalog.Pages
            .Select((page, pageIndex) => new SearchHit(page, pageIndex, page.Items[0], 0, IsCategory: true))
            .Where(hit => hit.Page.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        var itemHits = _catalog.Pages
            .SelectMany((page, pageIndex) => page.Items.Select((item, itemIndex) =>
                new SearchHit(page, pageIndex, item, itemIndex)))
            .Where(hit => Matches(query, hit.Item));
        SearchBox.ItemsSource = categoryHits.Concat(itemHits).Take(10).ToList();
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchHit hit)
            NavigateToHit(hit);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SearchHit chosenHit)
        {
            NavigateToHit(chosenHit);
            return;
        }

        var query = args.QueryText.Trim();
        if (query.Length == 0)
            return;
        var hit = _catalog.Pages
            .Select((page, pageIndex) => new SearchHit(page, pageIndex, page.Items[0], 0, IsCategory: true))
            .FirstOrDefault(result => result.Page.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            ?? _catalog.Pages.SelectMany((page, pageIndex) => page.Items.Select((item, itemIndex) =>
                    new SearchHit(page, pageIndex, item, itemIndex)))
                .FirstOrDefault(result => Matches(query, result.Item));
        if (hit is not null)
            NavigateToHit(hit);
    }

    private void NavigateToHit(SearchHit hit)
    {
        _selectedCategory = hit.Page;
        _isRefreshing = true;
        CategoryBox.SelectedIndex = hit.PageIndex;
        _isRefreshing = false;
        if (hit.IsCategory)
        {
            RefreshFurnitureList();
            ClearFurnitureEditor();
            return;
        }
        RefreshFurnitureList(hit.ItemIndex);
        _selectedItem = hit.Item;
        LoadFurnitureEditor(hit.ItemIndex);
    }

    private static bool Matches(string query, CatalogItem item) =>
        item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        item.Beco.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        item.Ref.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        item.Price.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void EmployeeSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        var query = sender.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            sender.ItemsSource = Array.Empty<object>();
            return;
        }

        var branchHits = _catalog.EmployeePages
            .Select((branch, branchIndex) => new EmployeeSearchHit(branch, branchIndex, null, -1))
            .Where(hit => hit.Branch.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        var employeeHits = _catalog.EmployeePages
            .SelectMany((branch, branchIndex) => branch.Employees.Select((employee, employeeIndex) =>
                new EmployeeSearchHit(branch, branchIndex, employee, employeeIndex)))
            .Where(hit => hit.Employee!.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          hit.Employee.Number.Contains(query, StringComparison.OrdinalIgnoreCase));
        sender.ItemsSource = branchHits.Concat(employeeHits).Take(10).ToList();
    }

    private void EmployeeSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is EmployeeSearchHit hit)
            NavigateToEmployeeHit(hit);
    }

    private void EmployeeSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var hit = args.ChosenSuggestion as EmployeeSearchHit;
        if (hit is null)
        {
            var query = args.QueryText.Trim();
            if (query.Length == 0)
                return;
            hit = _catalog.EmployeePages
                .Select((branch, branchIndex) => new EmployeeSearchHit(branch, branchIndex, null, -1))
                .FirstOrDefault(result => result.Branch.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                ?? _catalog.EmployeePages.SelectMany((branch, branchIndex) =>
                    branch.Employees.Select((employee, employeeIndex) =>
                        new EmployeeSearchHit(branch, branchIndex, employee, employeeIndex)))
                    .FirstOrDefault(result =>
                        result.Employee!.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        result.Employee.Number.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (hit is not null)
            NavigateToEmployeeHit(hit);
    }

    private void NavigateToEmployeeHit(EmployeeSearchHit hit)
    {
        _isRefreshing = true;
        BranchBox.SelectedIndex = hit.BranchIndex;
        _isRefreshing = false;
        _selectedBranch = hit.Branch;
        _selectedEmployee = hit.Employee;
        RefreshEmployeeList(hit.EmployeeIndex);
        LoadEmployeeEditor();
    }

    private async void AddCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var name = await AskForTextAsync("Neue Kategorie", "Name der Kategorie", "Kategorie");
        if (string.IsNullOrWhiteSpace(name))
            return;
        var page = CatalogPage.Create(name.Trim());
        _catalog.Pages.Add(page);
        _selectedCategory = page;
        SaveCatalog();
        RefreshCategories();
        ShowStatus($"Kategorie „{page.Name}“ wurde angelegt.");
    }

    private async void DeleteCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory is null || _catalog.Pages.Count <= 1)
        {
            ShowStatus("Mindestens eine Kategorie muss vorhanden sein.", InfoBarSeverity.Warning);
            return;
        }
        if (!await ConfirmAsync("Kategorie löschen", $"„{_selectedCategory.Name}“ und ihre 15 Positionen wirklich löschen?", "Kategorie löschen"))
            return;
        _catalog.Pages.Remove(_selectedCategory);
        _selectedCategory = _catalog.Pages[0];
        SaveCatalog();
        RefreshCategories();
        ClearFurnitureEditor();
    }

    private async void ChoosePhotoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            ShowStatus("Wähle zuerst eine Artikelposition aus.", InfoBarSeverity.Warning);
            return;
        }
        var picker = new FileOpenPicker();
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff" })
            picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;
        try
        {
            _selectedItem.Photo = _photoLibrary.AddPhoto(file.Path, _catalog);
            SaveCatalog();
            RefreshFurnitureList(_selectedCategory?.Items.IndexOf(_selectedItem) ?? -1);
            LoadPhotoPreview();
            ShowStatus("Foto wurde in die lokale Mediathek übernommen.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Foto konnte nicht übernommen werden: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void OpenPhotoLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            ShowStatus("Wähle zuerst eine Artikelposition aus.", InfoBarSeverity.Warning);
            return;
        }

        var photos = _photoLibrary.GetAvailablePhotos();
        if (photos.Count == 0)
        {
            ShowStatus("Die lokale Fotomediathek ist noch leer.", InfoBarSeverity.Informational);
            return;
        }

        var list = new ListView
        {
            Height = 520,
            SelectionMode = ListViewSelectionMode.Single
        };
        foreach (var photo in photos)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 4, 8, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(photo.FullPath)),
                Width = 48,
                Height = 48,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            });
            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            details.Children.Add(new TextBlock { Text = photo.FileName, TextTrimming = TextTrimming.CharacterEllipsis });
            details.Children.Add(new TextBlock
            {
                Text = photo.Key,
                FontSize = 12,
                Opacity = 0.7
            });
            Grid.SetColumn(details, 1);
            row.Children.Add(details);
            list.Items.Add(new ListViewItem { Content = row, Tag = photo });
        }

        var dialog = new ContentDialog
        {
            Title = "Fotomediathek",
            Content = list,
            PrimaryButtonText = "Foto verwenden",
            SecondaryButtonText = "Foto löschen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            if (list.SelectedItem is not ListViewItem selectedForRemoval ||
                selectedForRemoval.Tag is not PhotoLibraryEntry photoToRemove)
            {
                ShowStatus("Wähle zuerst ein Foto aus, das gelöscht werden soll.", InfoBarSeverity.Warning);
                return;
            }

            var references = CatalogData.GetPhotoReferences(_catalog, photoToRemove.Key);
            var affectedItems = string.Join("\n", references.Take(8).Select(reference =>
                $"{reference.Category} – {PhotoReferenceTitle(reference)} (Position {reference.Position})"));
            var message = references.Count == 0
                ? $"Das Foto „{photoToRemove.FileName}“ wird dauerhaft aus der lokalen Mediathek gelöscht."
                : $"Das Foto wird von {references.Count} Artikel(n) verwendet. Beim Löschen werden deren Fotofelder geleert:\n\n" +
                  affectedItems +
                  (references.Count > 8 ? $"\n… und {references.Count - 8} weitere" : "");
            if (!await ConfirmAsync("Foto löschen", message, "Foto löschen"))
                return;

            try
            {
                var removedReferences = _photoLibrary.RemovePhoto(_catalog, photoToRemove.Key);
                SaveCatalog();
                var selectedIndex = _selectedCategory?.Items.IndexOf(_selectedItem) ?? -1;
                RefreshFurnitureList(selectedIndex);
                LoadPhotoPreview();
                ShowStatus(removedReferences == 0
                    ? "Foto wurde aus der Mediathek gelöscht."
                    : $"Foto gelöscht; Fotofelder von {removedReferences} Artikel(n) wurden geleert.");
            }
            catch (Exception ex)
            {
                ShowStatus($"Foto konnte nicht gelöscht werden: {ex.Message}", InfoBarSeverity.Error);
            }
            return;
        }

        if (result != ContentDialogResult.Primary ||
            list.SelectedItem is not ListViewItem selected ||
            selected.Tag is not PhotoLibraryEntry photoEntry)
            return;

        _catalog.Library[photoEntry.Key] = photoEntry.FileName;
        _selectedItem.Photo = photoEntry.Key;
        SaveCatalog();
        RefreshFurnitureList(_selectedCategory?.Items.IndexOf(_selectedItem) ?? -1);
        LoadPhotoPreview();
        ShowStatus("Foto aus der lokalen Mediathek wurde zugeordnet.");
    }

    private void ClearPhotoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
            return;
        _selectedItem.Photo = "";
        PhotoPreview.Source = null;
        SaveCatalog();
        RefreshFurnitureList(_selectedCategory?.Items.IndexOf(_selectedItem) ?? -1);
    }

    private void LoadPhotoPreview()
    {
        PhotoPreview.Source = null;
        if (_selectedItem is null)
            return;
        var path = _photoLibrary.GetPhotoPath(_catalog, _selectedItem.Photo);
        if (path is null)
            return;
        try
        {
            PhotoPreview.Source = new BitmapImage(new Uri(path));
        }
        catch (Exception ex)
        {
            ShowStatus($"Das Foto konnte nicht angezeigt werden: {ex.Message}", InfoBarSeverity.Warning);
        }
    }

    private void RefreshBranches()
    {
        _isRefreshing = true;
        BranchBox.ItemsSource = _catalog.EmployeePages.Select(page => page.Name).ToList();
        if (_selectedBranch is null || !_catalog.EmployeePages.Contains(_selectedBranch))
            _selectedBranch = _catalog.EmployeePages[0];
        BranchBox.SelectedIndex = _catalog.EmployeePages.IndexOf(_selectedBranch);
        LoadEmployeeLayout();
        _isRefreshing = false;
        RefreshEmployeeList();
    }

    private void LoadEmployeeLayout()
    {
        _catalog.EmployeeLayout ??= new EmployeeLayout();
        EmployeeQrPositionBox.ItemsSource = new[] { "Rechts", "Links" };
        EmployeeCardWidthBox.Value = _catalog.EmployeeLayout.Width;
        EmployeeCardHeightBox.Value = _catalog.EmployeeLayout.Height;
        EmployeeNameSizeBox.Value = _catalog.EmployeeLayout.NameSize;
        EmployeeNumberSizeBox.Value = _catalog.EmployeeLayout.NumberSize;
        EmployeeQrPositionBox.SelectedIndex = string.Equals(_catalog.EmployeeLayout.QrPosition, "left", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        EmployeeNameBoldBox.IsChecked = _catalog.EmployeeLayout.NameBold;
    }

    private void EmployeeLayout_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isRefreshing || double.IsNaN(args.NewValue))
            return;
        if (ReferenceEquals(sender, EmployeeCardWidthBox)) _catalog.EmployeeLayout.Width = Math.Clamp(args.NewValue, 35, 180);
        else if (ReferenceEquals(sender, EmployeeCardHeightBox)) _catalog.EmployeeLayout.Height = Math.Clamp(args.NewValue, 12, 80);
        else if (ReferenceEquals(sender, EmployeeNameSizeBox)) _catalog.EmployeeLayout.NameSize = Math.Clamp(args.NewValue, 4, 24);
        else if (ReferenceEquals(sender, EmployeeNumberSizeBox)) _catalog.EmployeeLayout.NumberSize = Math.Clamp(args.NewValue, 4, 24);
        SaveCatalog();
    }

    private void EmployeeQrPosition_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;
        _catalog.EmployeeLayout.QrPosition = EmployeeQrPositionBox.SelectedIndex == 1 ? "left" : "right";
        SaveCatalog();
    }

    private void EmployeeNameBold_Changed(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;
        _catalog.EmployeeLayout.NameBold = EmployeeNameBoldBox.IsChecked == true;
        SaveCatalog();
    }

    private void BranchBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || BranchBox.SelectedIndex < 0 || BranchBox.SelectedIndex >= _catalog.EmployeePages.Count)
            return;
        _selectedBranch = _catalog.EmployeePages[BranchBox.SelectedIndex];
        _selectedEmployee = null;
        RefreshEmployeeList();
        LoadEmployeeEditor();
    }

    private void RefreshEmployeeList(int selectedIndex = -1)
    {
        if (_selectedBranch is null)
            return;
        _isRefreshing = true;
        EmployeeList.ItemsSource = _selectedBranch.Employees
            .Select(employee => $"{employee.Number}   ·   {employee.Name}")
            .ToList();
        EmployeeList.SelectedIndex = selectedIndex;
        _isRefreshing = false;
    }

    private void EmployeeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || _selectedBranch is null || EmployeeList.SelectedIndex < 0)
            return;
        if (EmployeeList.SelectedIndex < _selectedBranch.Employees.Count)
        {
            _selectedEmployee = _selectedBranch.Employees[EmployeeList.SelectedIndex];
            LoadEmployeeEditor();
        }
    }

    private void LoadEmployeeEditor()
    {
        _isRefreshing = true;
        EmployeeNameBox.Text = _selectedEmployee?.Name ?? "";
        EmployeeNumberBox.Text = _selectedEmployee?.Number ?? "";
        EmployeeCodeBox.Text = _selectedEmployee?.Code ?? "";
        _isRefreshing = false;
    }

    private void EmployeeField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isRefreshing || _selectedEmployee is null)
            return;
        _selectedEmployee.Name = EmployeeNameBox.Text;
        _selectedEmployee.Number = EmployeeNumberBox.Text;
        _selectedEmployee.Code = EmployeeCodeBox.Text;
        var index = _selectedBranch?.Employees.IndexOf(_selectedEmployee) ?? -1;
        SaveCatalog();
        if (index >= 0)
        {
            _isRefreshing = true;
            EmployeeList.ItemsSource = _selectedBranch!.Employees.Select(employee => $"{employee.Number}   ·   {employee.Name}").ToList();
            EmployeeList.SelectedIndex = index;
            _isRefreshing = false;
        }
    }

    private async void AddBranchButton_Click(object sender, RoutedEventArgs e)
    {
        var name = await AskForTextAsync("Neue Filiale", "Name der Filiale", $"Filiale {_catalog.EmployeePages.Count + 1}");
        if (string.IsNullOrWhiteSpace(name))
            return;
        var branch = new EmployeePage { Name = name.Trim() };
        _catalog.EmployeePages.Add(branch);
        _selectedBranch = branch;
        SaveCatalog();
        RefreshBranches();
    }

    private async void DeleteBranchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBranch is null || _catalog.EmployeePages.Count <= 1)
        {
            ShowStatus("Mindestens eine Filiale muss vorhanden sein.", InfoBarSeverity.Warning);
            return;
        }
        if (!await ConfirmAsync("Filiale löschen", $"„{_selectedBranch.Name}“ und die enthaltenen Mitarbeitenden löschen?", "Filiale löschen"))
            return;
        _catalog.EmployeePages.Remove(_selectedBranch);
        _selectedBranch = _catalog.EmployeePages[0];
        SaveCatalog();
        RefreshBranches();
    }

    private void AddEmployeeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBranch is null)
            return;
        var employee = new Employee();
        _selectedBranch.Employees.Add(employee);
        _selectedEmployee = employee;
        SaveCatalog();
        RefreshEmployeeList(_selectedBranch.Employees.Count - 1);
        LoadEmployeeEditor();
        EmployeeNameBox.Focus(FocusState.Programmatic);
    }

    private async void DeleteEmployeeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBranch is null || _selectedEmployee is null)
        {
            ShowStatus("Wähle zuerst einen Mitarbeitenden aus.", InfoBarSeverity.Warning);
            return;
        }
        if (!await ConfirmAsync("Mitarbeiter löschen", $"„{_selectedEmployee.Name}“ wirklich aus der Filiale entfernen?", "Mitarbeiter löschen"))
            return;
        _selectedBranch.Employees.Remove(_selectedEmployee);
        _selectedEmployee = null;
        SaveCatalog();
        RefreshEmployeeList();
        LoadEmployeeEditor();
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;
        try
        {
            var json = await File.ReadAllTextAsync(file.Path);
            var imported = JsonSerializer.Deserialize<CatalogData>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("Die JSON-Datei enthält keinen gültigen Katalog.");
            imported.Normalize();
            var skippedPhotos = _photoLibrary.NormalizeCatalogForImport(imported, Path.GetDirectoryName(file.Path));
            if (!await ConfirmAsync("Katalog importieren", "Der aktuelle Katalog wird durch den Inhalt der ausgewählten Datei ersetzt. Fortfahren?", "Importieren"))
                return;
            _catalog = imported;
            _selectedCategory = _catalog.Pages[0];
            _selectedBranch = _catalog.EmployeePages[0];
            _selectedItem = null;
            _selectedEmployee = null;
            SaveCatalog();
            RefreshCategories();
            RefreshBranches();
            LoadEmployeeEditor();
            ClearFurnitureEditor();
            ShowStatus(skippedPhotos > 0
                ? $"Katalog importiert. {skippedPhotos} Foto(s) in einem nicht unterstützten Format wurden ausgelassen; die Artikeldaten wurden übernommen."
                : "Katalog wurde importiert und lokal gespeichert.",
                skippedPhotos > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus($"Import fehlgeschlagen: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = "funituren-katalog.json"
            };
            picker.FileTypeChoices.Add("JSON-Datei", [".json"]);
            InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;
            var exportCopy = _photoLibrary.CreateExportCopy(_catalog);
            var json = JsonSerializer.Serialize(exportCopy, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(file.Path, json);
            ShowStatus("JSON-Sicherung einschließlich verfügbarer Fotos wurde exportiert.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Export fehlgeschlagen: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategory is null)
            return;
        try
        {
            _catalog.FurniturePrintLayout.Normalize();
            var html = A4HtmlExporter.CreateFurnitureHtml(_catalog, _selectedCategory, _photoLibrary);
            await PreviewAndPrintAsync($"A4-Vorschau · {_selectedCategory.Name}", html);
        }
        catch (Exception ex)
        {
            ShowStatus($"Druckvorschau konnte nicht geöffnet werden: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void PrintAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _catalog.FurniturePrintLayout.Normalize();
            var html = A4HtmlExporter.CreateFurnitureHtml(_catalog, _catalog.Pages, _photoLibrary);
            await PreviewAndPrintAsync("A4-Vorschau · alle Kategorien", html);
        }
        catch (Exception ex)
        {
            ShowStatus($"Druckvorschau konnte nicht geöffnet werden: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void PrintEmployeesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBranch is null)
            return;
        try
        {
            var html = A4HtmlExporter.CreateEmployeesHtml(_selectedBranch, _catalog.EmployeeLayout);
            await PreviewAndPrintAsync($"A4-Vorschau · {_selectedBranch.Name}", html);
        }
        catch (Exception ex)
        {
            ShowStatus($"Druckvorschau konnte nicht geöffnet werden: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void PrintAllEmployeesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var html = A4HtmlExporter.CreateEmployeesHtml(_catalog.EmployeePages, _catalog.EmployeeLayout);
            await PreviewAndPrintAsync("A4-Vorschau · alle Filialen", html);
        }
        catch (Exception ex)
        {
            ShowStatus($"Druckvorschau konnte nicht geöffnet werden: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task PreviewAndPrintAsync(string title, string html)
    {
        var webView = new WebView2
        {
            Width = 1160,
            Height = 760
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = webView,
            PrimaryButtonText = "Drucken",
            CloseButtonText = "Schließen",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            MaxWidth = 1220,
            XamlRoot = XamlRoot
        };

        webView.NavigationCompleted += (_, args) =>
        {
            dialog.IsPrimaryButtonEnabled = args.IsSuccess;
            if (!args.IsSuccess)
                ShowStatus("Die Druckvorschau konnte nicht vollständig geladen werden.", InfoBarSeverity.Error);
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            try
            {
                webView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.System);
            }
            catch (Exception ex)
            {
                ShowStatus($"Der Windows-Druckdialog konnte nicht geöffnet werden: {ex.Message}", InfoBarSeverity.Error);
            }
        };

        var dialogOperation = dialog.ShowAsync();
        try
        {
            await webView.EnsureCoreWebView2Async();
            webView.NavigateToString(html);
        }
        catch
        {
            dialog.Hide();
            await dialogOperation;
            throw;
        }

        await dialogOperation;
    }

    private async void QrCodeZipButton_Click(object sender, RoutedEventArgs e)
    {
        var layout = _catalog.FurniturePrintLayout;
        layout.Normalize();
        var products = _catalog.Pages.SelectMany(page => page.Items)
            .Where(item => !string.IsNullOrWhiteSpace(A4HtmlExporter.GetQrContent(item, layout)))
            .ToList();
        if (products.Count == 0)
        {
            ShowStatus("Es wurden keine Artikel mit einem QR-/Barcodewert gefunden.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var picker = new FileSavePicker { SuggestedFileName = "etiketten-codes.zip" };
            picker.FileTypeChoices.Add("ZIP-Datei", [".zip"]);
            InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;
            await using var output = await file.OpenStreamForWriteAsync();
            output.SetLength(0);
            using var archive = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true);
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in products)
            {
                var content = A4HtmlExporter.GetQrContent(item, layout);
                var baseName = SafeFileName(content);
                counts.TryGetValue(baseName, out var count);
                counts[baseName] = ++count;
                var entry = archive.CreateEntry($"{baseName}{(count > 1 ? $" ({count})" : "")}.png");
                await using var stream = entry.Open();
                var bytes = A4HtmlExporter.CreateQrBytes(content);
                await stream.WriteAsync(bytes);
            }
            ShowStatus($"{products.Count} QR-/Barcode-Codes wurden als ZIP exportiert.");
        }
        catch (Exception ex)
        {
            ShowStatus($"QR-Export fehlgeschlagen: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void LoadLayoutControls()
    {
        if (_catalog.FurniturePrintLayout is null)
            _catalog.FurniturePrintLayout = new FurniturePrintLayout();
        _catalog.FurniturePrintLayout.Normalize();
        _isRefreshing = true;
        CellWidthBox.Value = _catalog.FurniturePrintLayout.CellWidthCm;
        CellHeightBox.Value = _catalog.FurniturePrintLayout.CellHeightCm;
        LayoutElementBox.ItemsSource = LayoutElementNames;
        if (LayoutElementBox.SelectedIndex < 0)
            LayoutElementBox.SelectedIndex = 0;
        LoadSelectedLayoutElement();
        _isRefreshing = false;
    }

    private void LayoutElementBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isRefreshing)
            LoadSelectedLayoutElement();
    }

    private void LoadSelectedLayoutElement()
    {
        var element = GetSelectedLayoutElement();
        if (element is null)
            return;
        _isRefreshing = true;
        ElementXBox.Maximum = _catalog.FurniturePrintLayout.CellWidthCm;
        ElementYBox.Maximum = _catalog.FurniturePrintLayout.CellHeightCm;
        ElementWidthBox.Maximum = _catalog.FurniturePrintLayout.CellWidthCm;
        ElementHeightBox.Maximum = _catalog.FurniturePrintLayout.CellHeightCm;
        ElementXBox.Value = element.X;
        ElementYBox.Value = element.Y;
        ElementWidthBox.Value = element.Width;
        ElementHeightBox.Value = element.Height;
        ElementFontSizeBox.Value = element.FontSize;
        ElementBoldCheckBox.IsChecked = element.Bold;
        _isRefreshing = false;
    }

    private void LayoutField_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isRefreshing || double.IsNaN(args.NewValue))
            return;
        var layout = _catalog.FurniturePrintLayout;
        if (ReferenceEquals(sender, CellWidthBox))
            layout.CellWidthCm = Math.Clamp(args.NewValue, 2, 5);
        else if (ReferenceEquals(sender, CellHeightBox))
            layout.CellHeightCm = Math.Clamp(args.NewValue, 2, 5);
        else
        {
            var element = GetSelectedLayoutElement();
            if (element is null)
                return;
            if (ReferenceEquals(sender, ElementXBox)) element.X = args.NewValue;
            else if (ReferenceEquals(sender, ElementYBox)) element.Y = args.NewValue;
            else if (ReferenceEquals(sender, ElementWidthBox)) element.Width = args.NewValue;
            else if (ReferenceEquals(sender, ElementHeightBox)) element.Height = args.NewValue;
            else if (ReferenceEquals(sender, ElementFontSizeBox)) element.FontSize = args.NewValue;
        }
        layout.Normalize();
        LoadSelectedLayoutElement();
        SaveCatalog();
    }

    private void LayoutBoldCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;
        var element = GetSelectedLayoutElement();
        if (element is null)
            return;
        element.Bold = ElementBoldCheckBox.IsChecked == true;
        SaveCatalog();
    }

    private PrintElementLayout? GetSelectedLayoutElement() => LayoutElementBox.SelectedIndex switch
    {
        0 => _catalog.FurniturePrintLayout.Photo,
        1 => _catalog.FurniturePrintLayout.Qr,
        2 => _catalog.FurniturePrintLayout.Title,
        3 => _catalog.FurniturePrintLayout.Beco,
        4 => _catalog.FurniturePrintLayout.Ref,
        5 => _catalog.FurniturePrintLayout.Price,
        6 => _catalog.FurniturePrintLayout.Ek,
        _ => null
    };

    private void RefreshUsers()
    {
        if (_isAdministrator)
            UsersList.ItemsSource = _store.GetUsernames().ToList();
    }

    private void UsersList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void CreateUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAdministrator)
            return;
        var username = NewUsernameBox.Text.Trim();
        var password = NewPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(username))
        {
            ShowStatus("Bitte einen Benutzernamen eingeben.", InfoBarSeverity.Warning);
            return;
        }
        try
        {
            _store.CreateUser(username, password);
            NewUsernameBox.Text = "";
            NewPasswordBox.Password = "";
            RefreshUsers();
            ShowStatus($"Benutzer „{username}“ wurde angelegt.");
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void DeleteUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAdministrator || UsersList.SelectedItem is not string username || _signedInUser is null)
        {
            ShowStatus("Wähle zuerst ein Benutzerkonto aus.", InfoBarSeverity.Warning);
            return;
        }
        if (!await ConfirmAsync("Benutzer löschen", $"Das lokale Konto „{username}“ wirklich löschen?", "Benutzer löschen"))
            return;
        try
        {
            _store.DeleteUser(username, _signedInUser);
            RefreshUsers();
            ShowStatus($"Benutzer „{username}“ wurde gelöscht.");
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        _signedInUser = null;
        _isAdministrator = false;
        UsernameBox.Text = "";
        PasswordInput.Password = "";
        SetAuthenticationMode();
    }

    private void SaveCatalog()
    {
        try
        {
            _catalog.Normalize();
            _store.SaveCatalog(_catalog);
            ItemSaveStatus.Text = "Gespeichert";
            ItemSaveStatus.Opacity = 0.8;
        }
        catch (Exception ex)
        {
            ShowStatus($"Speichern fehlgeschlagen: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        StatusInfoBar.Title = severity == InfoBarSeverity.Success ? "Gespeichert" : "Hinweis";
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private async Task<string?> AskForTextAsync(string title, string prompt, string initialValue)
    {
        var input = new TextBox { Text = initialValue, PlaceholderText = prompt };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(input, "DialogTextInput");
        var dialog = new ContentDialog
        {
            Title = title,
            Content = input,
            PrimaryButtonText = "Speichern",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }

    private async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static string PhotoReferenceTitle(PhotoReference reference) =>
        string.IsNullOrWhiteSpace(reference.Title) ? "(ohne Namen)" : reference.Title.Trim();

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Join(' ', new string(value.Select(character => invalid.Contains(character) ? ' ' : character).ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > 100)
            name = name[..100].Trim();
        return string.IsNullOrWhiteSpace(name) ? "Code" : name;
    }

    private sealed record SearchHit(
        CatalogPage Page,
        int PageIndex,
        CatalogItem Item,
        int ItemIndex,
        bool IsCategory = false)
    {
        public override string ToString() => IsCategory
            ? $"Kategorie · {Page.Name}"
            : $"{Page.Name} · {ItemIndex + 1:00} · {ItemSummary(Item)}";
    }

    private sealed record EmployeeSearchHit(EmployeePage Branch, int BranchIndex, Employee? Employee, int EmployeeIndex)
    {
        public override string ToString() => Employee is null
            ? $"Filiale · {Branch.Name}"
            : $"{Branch.Name} · {Employee.Name} · {Employee.Number}";
    }
}
