using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FuniturenEditor.Desktop;

public sealed class CatalogSetupWindow : Window
{
    private static readonly LayoutChoice[] FieldKinds =
    [
        new("Text", "Text"),
        new("Zahl", "Number"),
        new("Foto", "Photo"),
        new("QR-Code", "Qr"),
        new("Barcode", "Barcode")
    ];

    private readonly PhotoLibrary photoLibrary;
    private readonly TextBox catalogNameBox = new() { Text = "Mein Katalog" };
    private readonly DataGrid fieldsGrid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        SelectionMode = DataGridSelectionMode.Single,
        AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(247, 249, 251)),
        RowHeight = 38,
        MaxHeight = 280,
        Margin = new Thickness(0, 12, 0, 0)
    };
    private readonly StackPanel[] steps = [new(), new(), new()];
    private readonly TextBlock stepHeading = new();
    private readonly TextBlock stepDescription = new();
    private readonly TextBlock validationMessage = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(170, 45, 45)),
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 8, 0, 0)
    };
    private readonly Button backButton = new() { Content = "Zurück" };
    private readonly Button nextButton = new() { Content = "Weiter", IsDefault = true };
    private readonly Button finishButton = new() { Content = "Einrichtung abschließen", IsDefault = true };
    private readonly Button configureLayoutButton = new() { Content = "Vorlage einrichten" };
    private readonly TextBlock layoutStatus = new();
    private readonly List<SetupField> fields = [];
    private readonly FurniturePrintLayout layout;
    private bool layoutConfigured;
    private int currentStep;

    public CatalogData Result { get; private set; } = new();

    public CatalogSetupWindow(PhotoLibrary photoLibrary)
    {
        this.photoLibrary = photoLibrary;
        layout = CreateDefaultLayout();
        fields.AddRange(layout.Fields.Select(SetupField.FromLayout));

        Title = "Katalog einrichten";
        Width = 680;
        Height = 520;
        MinWidth = 600;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(242, 245, 248));

        BuildFieldsGrid();
        BuildSteps();
        Content = BuildContent();
        UpdateStep();

        finishButton.Click += FinishSetup;
        configureLayoutButton.Click += ConfigureLayout;
        Loaded += (_, _) =>
        {
            catalogNameBox.Focus();
            catalogNameBox.SelectAll();
        };
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock
        {
            Text = "Erstelle deinen ersten Katalog",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 43, 66))
        });
        header.Children.Add(stepHeading);
        header.Children.Add(stepDescription);
        header.Children.Add(validationMessage);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        backButton.Click += (_, _) => ChangeStep(-1);
        nextButton.Click += (_, _) => ChangeStep(1);
        Grid.SetColumn(backButton, 0);
        Grid.SetColumn(nextButton, 2);
        Grid.SetColumn(finishButton, 3);
        footer.Children.Add(backButton);
        footer.Children.Add(nextButton);
        footer.Children.Add(finishButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        foreach (var step in steps)
        {
            step.Visibility = Visibility.Collapsed;
            step.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(step);
        }

        return root;
    }

    private void BuildSteps()
    {
        steps[0].Children.Add(new TextBlock
        {
            Text = "Gib deinem Katalog einen Namen. Du kannst ihn später jederzeit ändern.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(67, 87, 106)),
            Margin = new Thickness(0, 12, 0, 8)
        });
        catalogNameBox.MinHeight = 40;
        catalogNameBox.FontSize = 15;
        steps[0].Children.Add(catalogNameBox);

        var fieldsPanel = new DockPanel();
        var fieldActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var addFieldButton = new Button { Content = "Feld hinzufügen" };
        var removeFieldButton = new Button { Content = "Feld entfernen", Margin = new Thickness(0) };
        addFieldButton.Click += (_, _) =>
        {
            CommitFields();
            var field = CreateField($"Neues Feld {fields.Count + 1}", "Text", 0.15, 0.15 + fields.Count * 0.4);
            fields.Add(SetupField.FromLayout(field));
            fieldsGrid.ItemsSource = null;
            fieldsGrid.ItemsSource = fields;
            fieldsGrid.SelectedItem = fields[^1];
            layoutConfigured = false;
            UpdateStep();
        };
        removeFieldButton.Click += (_, _) =>
        {
            if (fieldsGrid.SelectedItem is not SetupField selected)
                return;
            fields.Remove(selected);
            fieldsGrid.ItemsSource = null;
            fieldsGrid.ItemsSource = fields;
            layoutConfigured = false;
            UpdateStep();
        };
        fieldActions.Children.Add(addFieldButton);
        fieldActions.Children.Add(removeFieldButton);
        DockPanel.SetDock(fieldActions, Dock.Top);
        fieldsPanel.Children.Add(fieldActions);
        fieldsPanel.Children.Add(fieldsGrid);
        steps[1].Children.Add(fieldsPanel);
        steps[1].Children.Add(new TextBlock
        {
            Text = "Felder bestimmen, welche Angaben du pro Eintrag speichern kannst. Zahl-Felder können z. B. ein €-Suffix erhalten.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(67, 87, 106)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var templatePanel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        templatePanel.Children.Add(new TextBlock
        {
            Text = "Lege Papierformat, Ausrichtung, Raster und die Position der Felder auf dem Etikett fest.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(67, 87, 106)),
            Margin = new Thickness(0, 0, 0, 12)
        });
        configureLayoutButton.HorizontalAlignment = HorizontalAlignment.Left;
        templatePanel.Children.Add(configureLayoutButton);
        layoutStatus.TextWrapping = TextWrapping.Wrap;
        layoutStatus.Margin = new Thickness(0, 10, 0, 0);
        templatePanel.Children.Add(layoutStatus);
        steps[2].Children.Add(templatePanel);
    }

    private void BuildFieldsGrid()
    {
        fieldsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Feldname",
            Binding = new Binding(nameof(SetupField.Name)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });
        fieldsGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Eigenschaft",
            ItemsSource = FieldKinds,
            DisplayMemberPath = nameof(LayoutChoice.Name),
            SelectedValuePath = nameof(LayoutChoice.Key),
            SelectedValueBinding = new Binding(nameof(SetupField.Kind)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        fieldsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Suffix",
            Binding = new Binding(nameof(SetupField.Suffix)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        fieldsGrid.ItemsSource = fields;
        fieldsGrid.CellEditEnding += (_, _) => layoutConfigured = false;
        fieldsGrid.CurrentCellChanged += (_, _) => UpdateStep();
    }

    private void ChangeStep(int direction)
    {
        if (direction > 0 && currentStep == 0 && string.IsNullOrWhiteSpace(catalogNameBox.Text))
        {
            ShowValidation("Gib zuerst einen Namen für den Katalog ein.");
            catalogNameBox.Focus();
            return;
        }

        if (direction > 0 && currentStep == 1 && !CommitFields())
            return;

        currentStep = Math.Clamp(currentStep + direction, 0, steps.Length - 1);
        UpdateStep();
    }

    private bool CommitFields()
    {
        fieldsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        fieldsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (fields.Count == 0)
        {
            ShowValidation("Der Katalog braucht mindestens ein Feld.");
            return false;
        }

        var duplicateName = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .GroupBy(field => field.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
        {
            ShowValidation($"Das Feld „{duplicateName.Key}“ ist mehrfach vorhanden.");
            return false;
        }

        for (var index = 0; index < fields.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(fields[index].Name))
                fields[index].Name = $"Feld {index + 1}";
        }

        var previousFields = layout.Fields.ToDictionary(field => field.Id, StringComparer.Ordinal);
        layout.Fields = fields.Select((field, index) =>
        {
            if (previousFields.TryGetValue(field.Id, out var existing))
            {
                existing.Name = field.Name.Trim();
                existing.Kind = field.Kind;
                existing.Source = "Custom";
                existing.Suffix = field.Kind == "Number" ? field.Suffix : "";
                return existing;
            }

            var position = 0.15 + index * 0.4;
            return CreateField(field.Name.Trim(), field.Kind, 0.15, position, suffix: field.Suffix);
        }).ToList();
        layout.Normalize();
        return true;
    }

    private void UpdateStep()
    {
        validationMessage.Text = "";
        validationMessage.Visibility = Visibility.Collapsed;
        for (var index = 0; index < steps.Length; index++)
            steps[index].Visibility = index == currentStep ? Visibility.Visible : Visibility.Collapsed;

        stepHeading.Text = currentStep switch
        {
            0 => "Schritt 1 von 3 · Name",
            1 => "Schritt 2 von 3 · Eigenschaften",
            _ => "Schritt 3 von 3 · Vorlage"
        };
        stepDescription.Text = currentStep switch
        {
            0 => "Wie soll dein erster Katalog heißen?",
            1 => "Passe die vorgeschlagenen Felder an deinen Arbeitsablauf an.",
            _ => "Zum Abschluss richtest du das Drucklayout für diesen Katalog ein."
        };
        stepDescription.Foreground = new SolidColorBrush(Color.FromRgb(101, 117, 134));
        stepDescription.Margin = new Thickness(0, 4, 0, 0);
        backButton.IsEnabled = currentStep > 0;
        nextButton.Visibility = currentStep < steps.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        nextButton.IsDefault = currentStep < steps.Length - 1;
        finishButton.Visibility = currentStep == steps.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        finishButton.IsDefault = currentStep == steps.Length - 1;
        finishButton.IsEnabled = layoutConfigured && fields.Count > 0;
        layoutStatus.Text = layoutConfigured
            ? "Vorlage übernommen. Du kannst die Einrichtung jetzt abschließen."
            : "Die Einrichtung der Vorlage ist noch erforderlich.";
    }

    private void ConfigureLayout(object sender, RoutedEventArgs e)
    {
        if (!CommitFields())
            return;

        var page = new CatalogPage { Name = "Liste 1", Items = [CreateSampleItem()] };
        var draftCatalog = new CatalogData
        {
            Catalogs = [new EditorCatalog { Name = catalogNameBox.Text.Trim(), Lists = [page], Layout = layout }]
        };
        var editor = new FurnitureLayoutWindow(draftCatalog, page.Items[0], photoLibrary, layout)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true)
            return;

        layout.CopyFrom(editor.Result);
        fields.Clear();
        fields.AddRange(layout.Fields.Select(SetupField.FromLayout));
        fieldsGrid.ItemsSource = null;
        fieldsGrid.ItemsSource = fields;
        layoutConfigured = true;
        UpdateStep();
    }

    private CatalogItem CreateSampleItem()
    {
        var item = new CatalogItem();
        foreach (var field in layout.Fields)
            item.FieldValues[field.Id] = field.Kind switch
            {
                "Photo" => "",
                "Number" => "19,90",
                _ => field.Name
            };
        return item;
    }

    private void FinishSetup(object sender, RoutedEventArgs e)
    {
        if (!CommitFields() || !layoutConfigured)
            return;
        if (string.IsNullOrWhiteSpace(catalogNameBox.Text))
        {
            currentStep = 0;
            UpdateStep();
            ShowValidation("Gib einen Namen für den Katalog ein.");
            catalogNameBox.Focus();
            return;
        }

        var catalog = new EditorCatalog
        {
            Name = catalogNameBox.Text.Trim(),
            Layout = layout.Copy(),
            Lists =
            [
                new CatalogPage
                {
                    Name = "Liste 1",
                    Items = Enumerable.Range(0, layout.TileCount).Select(_ => new CatalogItem()).ToList()
                }
            ]
        };
        Result = new CatalogData { Catalogs = [catalog] };
        Result.Normalize();
        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        validationMessage.Text = message;
        validationMessage.Visibility = Visibility.Visible;
    }

    private static FurniturePrintLayout CreateDefaultLayout()
    {
        var name = CreateField("Name", "Text", 0.15, 0.15, width: 3.2, height: 0.55, bold: true);
        var articleNumber = CreateField("Artikelnummer", "Text", 0.15, 0.8, width: 3.2, height: 0.35);
        var price = CreateField("Preis", "Number", 0.15, 2.85, width: 1.5, height: 0.4, suffix: " €", bold: true);
        var photo = CreateField("Foto", "Photo", 2.5, 1.2, width: 0.8, height: 0.8);
        var result = new FurniturePrintLayout
        {
            Fields = [name, articleNumber, price, photo]
        };
        result.QrCode.SourceField = articleNumber.Id;
        result.QrCode.FallbackField = name.Id;
        result.Normalize();
        return result;
    }

    private static PrintElementLayout CreateField(
        string name,
        string kind,
        double x,
        double y,
        double width = 3.2,
        double height = 0.35,
        string suffix = "",
        bool bold = false) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Kind = kind,
        Source = "Custom",
        X = x,
        Y = y,
        Width = width,
        Height = height,
        FontSize = kind == "Number" ? 7 : 6,
        Suffix = suffix,
        Bold = bold
    };

    private sealed class SetupField
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "Text";
        public string Suffix { get; set; } = "";

        public static SetupField FromLayout(PrintElementLayout field) => new()
        {
            Id = field.Id,
            Name = field.Name,
            Kind = field.Kind,
            Suffix = field.Suffix
        };
    }

    private sealed record LayoutChoice(string Name, string Key);
}
