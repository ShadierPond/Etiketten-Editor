using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LabelEditor.Desktop;

public sealed class HelpWindow : Window
{
    private static readonly string[] Categories =
    [
        "Alle Themen",
        "Erste Schritte",
        "Ersteinrichtung",
        "Kataloge und Listen",
        "Einträge bearbeiten",
        "Suche und Sortierung",
        "Vorlage und Fotos",
        "Vorlage – Allgemein",
        "Drucken und PDF",
        "Daten sichern",
        "Einstellungen und Updates"
    ];

    private static readonly HelpArticle[] Articles =
    [
        new("Erste Schritte", "So ist der Editor aufgebaut",
            "Kataloge enthalten mehrere Listen. In den Listen pflegst du deine Etiketteneinträge und legst fest, wie sie gedruckt werden.",
            ["Wähle oben den gewünschten Katalog.", "Wähle darunter eine Liste oder lege eine neue an.", "Bearbeite die Werte direkt in der Tabelle. Änderungen werden automatisch gespeichert."],
            "Mit der Suche findest du Einträge katalogübergreifend. Die Druckvorschau rechts zeigt das aktuelle Etikettenlayout.",
            "start",
            [new("assets/help/main-editor-overview.png", "Hauptfenster: Katalog, Liste, Etikettentabelle und Druckvorschau.")]),
        new("Ersteinrichtung", "Den ersten Katalog einrichten",
            "Beim ersten Start führt dich der Einrichtungsassistent durch Name, Eigenschaften und Etikettenvorlage.",
            ["Gib deinem Katalog einen Namen. Du kannst ihn später jederzeit ändern.", "Prüfe die vorgeschlagenen Eigenschaften und füge bei Bedarf Felder hinzu oder entferne sie.", "Richte im letzten Schritt das Drucklayout ein: Papierformat, Ausrichtung, Raster und Position der Felder.", "Schließe die Einrichtung ab, sobald die Vorlage eingerichtet ist."],
            "Eigenschaften bestimmen, welche Angaben du pro Etikett speichern kannst. Zahlenfelder können zum Beispiel ein €-Suffix erhalten.",
            "start",
            [
                new("assets/help/setup-catalog-name.png", "Schritt 1: Namen für den ersten Katalog eingeben."),
                new("assets/help/setup-fields.png", "Schritt 2: Eigenschaften wie Name, Artikelnummer, Preis und Foto festlegen."),
                new("assets/help/setup-template.png", "Schritt 3: Vorlage einrichten, bevor der erste Katalog angelegt wird.")
            ]),
        new("Kataloge und Listen", "Kataloge und Listen verwalten",
            "Kataloge trennen Sortimente; Listen teilen einen Katalog in übersichtliche Bereiche.",
            ["Wähle ein Sortiment im Katalogfeld.", "Öffne „Katalog ▾“, um einen Katalog hinzuzufügen, umzubenennen oder zu löschen.", "Wähle eine Liste und verwalte sie über „Liste ▾“."],
            "Löschen kann Daten entfernen. Prüfe deshalb den ausgewählten Katalog oder die Liste vor dem Bestätigen.",
            "catalogs",
            [
                new("assets/help/main-editor-overview.png", "Katalog- und Listenauswahl im Hauptfenster."),
                new("assets/help/catalog-menu.png", "Katalogmenü: Katalog hinzufügen, umbenennen oder löschen."),
                new("assets/help/list-menu.png", "Listenmenü: Liste hinzufügen, umbenennen oder löschen.")
            ]),
        new("Einträge bearbeiten", "Etiketteneinträge pflegen",
            "Jede Tabellenzeile ist ein Etiketteneintrag. Die Spalten entsprechen den Feldern der Katalogvorlage.",
            ["Klicke in eine Zelle und ändere den Wert.", "Mit Enter oder einem Klick in eine andere Zelle wird die Eingabe übernommen.", "Ziehe die Positionsnummer, um einen Eintrag innerhalb der Liste neu anzuordnen."],
            "Über das Kontextmenü eines Eintrags kannst du – abhängig von den vorhandenen Daten – ein Bild oder einen QR-/Barcode speichern.",
            "items",
            [new("assets/help/label-table-example.png", "Tabellenansicht: Einträge bearbeiten und ein Foto zuordnen.")]),
        new("Suche und Sortierung", "Einträge schnell finden",
            "Die Suchleiste durchsucht Kataloge und Listen. Spaltenköpfe sortieren die angezeigte Liste.",
            ["Gib einen Suchbegriff in „Suchen“ ein.", "Wähle einen Treffer aus, um zum passenden Katalog und Eintrag zu springen.", "Klicke auf einen Spaltenkopf, um auf- oder absteigend zu sortieren."],
            "Zahlenfelder werden numerisch sortiert. Die Positionsnummer lässt sich durch Ziehen verändern.",
            "search",
            [new("assets/help/search-header-example.png", "Kopfbereich der App mit Suchfeld.")]),
        new("Vorlage und Fotos", "Etikettenvorlage gestalten",
            "Über „Vorlage“ legst du fest, welche Felder auf einem Etikett erscheinen und wie sie angeordnet sind.",
            ["Öffne „Vorlage“ und wähle den Bereich „Katalog“, um Felder zu bearbeiten.", "Wähle ein Feld in der Vorschau aus. Ziehe es zum Verschieben oder nutze die Ziehpunkte zum Ändern der Größe."],
            "In der Fotobibliothek gespeicherte Bilder können den Etiketteneinträgen zugeordnet werden.",
            "template",
            [
                new("assets/help/template-catalog-fields.png", "Vorlageneditor: Feld auswählen und seine Position und Größe in der Etikettenvorschau sehen."),
                new("assets/help/template-field-types.png", "Feldtypen: Text, Zahl, Foto, QR-Code oder Barcode."),
                new("assets/help/template-field-properties.png", "Feldposition, Abmessungen, Schrift und Ausrichtung einstellen."),
                new("assets/help/main-template-button.png", "Über „Vorlage“ öffnest du den Vorlageneditor.")
            ]),
        new("Vorlage – Allgemein", "Vorlage – Allgemeine Einstellungen",
            "Im Reiter „Allgemein“ bestimmst du Papierformat, Ausrichtung, Platzierung des Firmenlogos und das Druckraster. Die Bilder zeigen die echten Einstellungen der App.",
            ["Wähle Hoch- oder Querformat und das gewünschte Blattformat.", "Lege fest, wo das Kachelraster auf dem Blatt ausgerichtet wird.", "Wähle ein Firmenlogo und positioniere oder skaliere es in der Blattvorschau.", "Passe Breite, Höhe, Abstände, Spalten und Einträge je Liste an."],
            "Breite und Höhe beziehen sich auf die Etiketten. Das Firmenlogo wird auf jedem Blatt ausgegeben.",
            "template-general",
            [
                new("assets/help/template-general-overview.png", "Überblick: Blatt- und Druckraster, Logo und Blattvorschau."),
                new("assets/help/paper-format-options.png", "Blattformat: Wähle zum Beispiel DIN A3, A4, A5, A6, Letter oder Legal."),
                new("assets/help/orientation-options.png", "Ausrichtung: Querformat oder Hochformat."),
                new("assets/help/company-logo-controls.png", "Firmenlogo: auswählen oder aus der Vorlage entfernen."),
                new("assets/help/dimensions-and-grid.png", "Etikettengröße, Abstände, Spalten und Einträge je Liste."),
                new("assets/help/sheet-preview.png", "Die Blattvorschau zeigt das Druckraster auf dem gewählten Papier."),
                new("assets/help/grid-alignment-options.png", "Rasterausrichtung: Das Kachelraster links, mittig oder rechts sowie oben, mittig oder unten platzieren.")
            ]),
        new("Vorlage und Fotos", "Fotos verwalten und zuordnen",
            "Die Fotobibliothek verwaltet Bilder zentral. Von dort aus kannst du Fotos passenden Einträgen zuweisen.",
            ["Öffne die Fotobibliothek über die Bildauswahl eines Eintrags.", "Importiere ein Bild und wähle es in der Bibliothek aus.", "Bestätige die Auswahl, um es dem Etiketteneintrag zuzuordnen."],
            "Firmenlogo und Druckraster richtest du unter „Vorlage → Allgemein“ ein. Beim Löschen eines Bildes zeigt der Editor an, wenn es noch verwendet wird.",
            "photos",
            [
                new("assets/help/photo-library.png", "Fotomediathek: Fotos durchsuchen, sortieren und hinzufügen."),
                new("assets/help/photo-library-selection.png", "Ausgewähltes Foto: Vorschau, Dateiname bearbeiten oder Foto verwenden."),
                new("assets/help/photo-library-empty-example.png", "Leere Fotomediathek mit der Schaltfläche zum Hinzufügen von Fotos.")
            ]),
        new("Drucken und PDF", "Eine Liste oder den ganzen Katalog drucken",
            "Druck- und PDF-Aktionen bieten jeweils die Auswahl zwischen der aktuellen Liste und dem ganzen Katalog.",
            ["Öffne „Drucken ▾“ oder „Als PDF speichern ▾“.", "Wähle „Aktuelle Liste“ oder „Ganzen Katalog“.", "Prüfe die Druckvorschau. Beim PDF-Speichern wählst du anschließend den Dateinamen und Speicherort."],
            "Die Seitenzählung „Seite X von Y“ wird nur beim ganzen Katalog eingeblendet. Die aktuelle Liste bleibt ohne Seitenangabe.",
            "print",
            [
                new("assets/help/pdf-export-menu.png", "PDF-Menü: aktuelle Liste oder ganzen Katalog auswählen."),
                new("assets/help/print-preview.png", "Druckvorschau vor dem Öffnen des Windows-Druckdialogs.")
            ]),
        new("Daten sichern", "Kataloge importieren und exportieren",
            "Über „Einstellungen ▾“ kannst du Katalogdaten importieren oder exportieren.",
            ["Öffne „Einstellungen ▾“.", "Wähle „Katalog exportieren“, um eine Sicherung zu erstellen.", "Mit „Katalog importieren“ kannst du vorhandene Katalogdaten einlesen."],
            "Bewahre exportierte Sicherungen an einem sicheren Ort auf.",
            "backup",
            [new("assets/help/settings-menu.png", "Einstellungen: Kataloge importieren oder exportieren und weitere Optionen öffnen.")]),
        new("Einstellungen und Updates", "Nach Updates suchen",
            "Die App kann nach einer neueren Version im öffentlichen GitHub-Repository suchen.",
            ["Öffne „Einstellungen ▾“ und wähle „Nach Updates suchen“.", "Klicke im Fenster „Softwareupdates“ auf „Nach Updates suchen“.", "Wenn ein Update verfügbar ist, kannst du es herunterladen und zur Installation vorbereiten."],
            "Für die Updateprüfung ist keine Anmeldung erforderlich. Installiere nur ein Update, wenn du es erwartest.",
            "backup",
            [
                new("assets/help/settings-menu.png", "Öffne die Updateprüfung über „Einstellungen ▾“."),
                new("assets/help/update-dialog.png", "Im Fenster „Softwareupdates“ die Prüfung starten.")
            ]),
        new("Einstellungen und Updates", "Auf Werkseinstellungen zurücksetzen",
            "Das Zurücksetzen entfernt alle Kataloge, Listen, Einträge und Fotos aus der Anwendung. Danach beginnt die Einrichtung eines neuen ersten Katalogs.",
            ["Öffne „Einstellungen ▾“ und wähle „Auf Werkseinstellungen zurücksetzen“.", "Lies den Bestätigungsdialog sorgfältig.", "Wenn du fortfährst, werden die aufgeführten Daten entfernt und die Ersteinrichtung startet erneut."],
            "Dieser Vorgang entfernt deine lokalen Kataloge, Listen, Einträge und Fotos. Erstelle vorher einen Katalogexport, wenn du die Daten behalten möchtest.",
            "backup",
            [
                new("assets/help/settings-menu.png", "Der Zurücksetzen-Befehl befindet sich im Menü „Einstellungen“."),
                new("assets/help/reset-confirmation.png", "Bestätigungsdialog: Alle Kataloge, Listen, Einträge und Fotos werden entfernt.")
            ])
    ];

    private readonly TextBox searchBox = new() { Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox categoryBox = new() { Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ListBox articleList = new();
    private readonly TextBlock articleCategory = new();
    private readonly TextBlock articleTitle = new();
    private readonly TextBlock articleSummary = new();
    private readonly TextBlock articleTip = new();
    private readonly StackPanel stepsPanel = new();
    private readonly ContentControl illustrationHost = new();
    private readonly Button previousScreenshotButton = new() { Content = "‹", MinWidth = 34, Padding = new Thickness(4) };
    private readonly Button nextScreenshotButton = new() { Content = "›", MinWidth = 34, Padding = new Thickness(4) };
    private readonly Image screenshotImage = new() { Stretch = Stretch.Uniform, MaxHeight = 310 };
    private readonly TextBlock screenshotCaption = new();
    private readonly TextBlock screenshotCount = new();
    private Border? screenshotGallery;
    private readonly Button previousButton = new() { Content = "Zurück", MinWidth = 94 };
    private readonly Button nextButton = new() { Content = "Weiter", MinWidth = 94 };
    private readonly TextBlock pageCount = new();
    private List<HelpArticle> visibleArticles = [];
    private HelpArticle? displayedArticle;
    private int screenshotIndex;

    public HelpWindow()
    {
        Title = "Hilfe und Anleitung";
        Width = 1040;
        Height = 720;
        MinWidth = 800;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#F2F5F8");
        FontFamily = new FontFamily("Segoe UI");
        Resources.Add(typeof(ScrollBar), new Style(typeof(ScrollBar),
            (Style)Application.Current.FindResource("ModernScrollBar")));

        BuildInterface();
        searchBox.TextChanged += (_, _) => RefreshArticles();
        categoryBox.SelectionChanged += (_, _) => RefreshArticles();
        articleList.SelectionChanged += (_, _) => ShowSelectedArticle();
        previousButton.Click += (_, _) => MoveSelection(-1);
        nextButton.Click += (_, _) => MoveSelection(1);
        previousScreenshotButton.Click += (_, _) => MoveScreenshot(-1);
        nextScreenshotButton.Click += (_, _) => MoveScreenshot(1);
        categoryBox.ItemsSource = Categories;
        categoryBox.SelectedIndex = 0;
    }

    private void BuildInterface()
    {
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock
        {
            Text = "Hilfe und Anleitung",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#172B42")
        });
        header.Children.Add(new TextBlock
        {
            Text = "Thema auswählen, nach einem Begriff suchen oder Schritt für Schritt durchblättern.",
            FontSize = 13,
            Foreground = Brush("#657586"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        root.Children.Add(header);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var left = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D8E0E8"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 14, 0)
        };
        var leftPanel = new DockPanel();
        searchBox.Margin = new Thickness(0, 0, 0, 8);
        searchBox.Padding = new Thickness(9, 2, 9, 2);
        searchBox.ToolTip = "Anleitung nach Stichwort durchsuchen";
        searchBox.SetValue(AutomationProperties.NameProperty, "Anleitung durchsuchen");
        DockPanel.SetDock(searchBox, Dock.Top);
        leftPanel.Children.Add(searchBox);
        categoryBox.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(categoryBox, Dock.Top);
        leftPanel.Children.Add(categoryBox);
        articleList.BorderThickness = new Thickness(0);
        articleList.Background = Brushes.Transparent;
        articleList.DisplayMemberPath = nameof(HelpArticle.Title);
        ScrollViewer.SetVerticalScrollBarVisibility(articleList, ScrollBarVisibility.Auto);
        leftPanel.Children.Add(articleList);
        left.Child = leftPanel;
        content.Children.Add(left);

        var right = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D8E0E8"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(22)
        };
        Grid.SetColumn(right, 1);
        content.Children.Add(right);
        var articleScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var articlePanel = new StackPanel();
        articleCategory.Foreground = Brush("#1677D2");
        articleCategory.FontWeight = FontWeights.SemiBold;
        articleTitle.FontSize = 23;
        articleTitle.FontWeight = FontWeights.SemiBold;
        articleTitle.Foreground = Brush("#172B42");
        articleTitle.TextWrapping = TextWrapping.Wrap;
        articleTitle.Margin = new Thickness(0, 5, 0, 8);
        articleSummary.FontSize = 14;
        articleSummary.Foreground = Brush("#43576A");
        articleSummary.TextWrapping = TextWrapping.Wrap;
        articleSummary.Margin = new Thickness(0, 0, 0, 14);
        articlePanel.Children.Add(articleCategory);
        articlePanel.Children.Add(articleTitle);
        articlePanel.Children.Add(articleSummary);
        articlePanel.Children.Add(new TextBlock
        {
            Text = "Ansicht",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#43576A"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        illustrationHost.Margin = new Thickness(0, 0, 0, 18);
        articlePanel.Children.Add(illustrationHost);
        articlePanel.Children.Add(new TextBlock
        {
            Text = "Schritt für Schritt",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#172B42"),
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 7)
        });
        stepsPanel.Margin = new Thickness(0, 0, 0, 14);
        articlePanel.Children.Add(stepsPanel);
        articleTip.TextWrapping = TextWrapping.Wrap;
        articleTip.Foreground = Brush("#43576A");
        articleTip.Background = Brush("#EEF5FB");
        articleTip.Padding = new Thickness(12);
        articleTip.Margin = new Thickness(0, 0, 0, 8);
        articlePanel.Children.Add(articleTip);
        articleScroll.Content = articlePanel;
        right.Child = articleScroll;

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(previousButton);
        pageCount.Foreground = Brush("#657586");
        pageCount.VerticalAlignment = VerticalAlignment.Center;
        pageCount.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(pageCount, 1);
        footer.Children.Add(pageCount);
        nextButton.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(nextButton, 2);
        footer.Children.Add(nextButton);
        var closeButton = new Button { Content = "Schließen", MinWidth = 100, IsDefault = true };
        closeButton.Click += (_, _) => Close();
        Grid.SetColumn(closeButton, 3);
        footer.Children.Add(closeButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
    }

    private void RefreshArticles()
    {
        var category = categoryBox.SelectedItem as string ?? Categories[0];
        var query = searchBox.Text.Trim();
        visibleArticles = Articles.Where(article =>
                (category == Categories[0] || article.Category == category) &&
                (query.Length == 0 || article.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
        articleList.ItemsSource = visibleArticles;
        articleList.SelectedIndex = visibleArticles.Count > 0 ? 0 : -1;
        ShowSelectedArticle();
    }

    private void ShowSelectedArticle()
    {
        if (articleList.SelectedItem is not HelpArticle article)
        {
            articleCategory.Text = "";
            articleTitle.Text = "Keine passenden Themen gefunden";
            articleSummary.Text = "Ändere den Suchbegriff oder wähle eine andere Kategorie.";
            illustrationHost.Content = null;
            stepsPanel.Children.Clear();
            articleTip.Text = "";
            UpdateNavigation();
            return;
        }

        articleCategory.Text = article.Category;
        articleTitle.Text = article.Title;
        articleSummary.Text = article.Summary;
        if (!ReferenceEquals(displayedArticle, article))
        {
            displayedArticle = article;
            screenshotIndex = 0;
        }
        illustrationHost.Content = article.Screenshots is { Length: > 0 }
            ? BuildScreenshotGallery(article.Screenshots)
            : BuildIllustration(article.Illustration);
        stepsPanel.Children.Clear();
        for (var index = 0; index < article.Steps.Length; index++)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = Brush("#E8F1FA"),
                Child = new TextBlock
                {
                    Text = (index + 1).ToString(),
                    Foreground = Brush("#145B96"),
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            var stepText = new TextBlock
            {
                Text = article.Steps[index],
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#273E54"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            Grid.SetColumn(stepText, 1);
            row.Children.Add(stepText);
            stepsPanel.Children.Add(row);
        }
        articleTip.Text = article.Tip;
        UpdateNavigation();
    }

    private Border BuildScreenshotGallery(HelpScreenshot[] screenshots)
    {
        if (screenshotGallery is not null)
        {
            UpdateScreenshotGallery(screenshots);
            return screenshotGallery;
        }

        var imageFrame = new Border
        {
            Height = 310,
            Background = Brush("#F4F7FA"),
            BorderBrush = Brush("#D8E0E8"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = screenshotImage
        };
        var captionRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        captionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        captionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        captionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        captionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        previousScreenshotButton.ToolTip = "Vorheriger Screenshot";
        previousScreenshotButton.SetValue(AutomationProperties.NameProperty, "Vorheriger Screenshot");
        nextScreenshotButton.ToolTip = "Nächster Screenshot";
        nextScreenshotButton.SetValue(AutomationProperties.NameProperty, "Nächster Screenshot");
        captionRow.Children.Add(previousScreenshotButton);
        screenshotCaption.TextWrapping = TextWrapping.Wrap;
        screenshotCaption.VerticalAlignment = VerticalAlignment.Center;
        screenshotCaption.Foreground = Brush("#43576A");
        screenshotCaption.Margin = new Thickness(10, 0, 10, 0);
        Grid.SetColumn(screenshotCaption, 1);
        captionRow.Children.Add(screenshotCaption);
        screenshotCount.VerticalAlignment = VerticalAlignment.Center;
        screenshotCount.Foreground = Brush("#657586");
        Grid.SetColumn(screenshotCount, 2);
        captionRow.Children.Add(screenshotCount);
        nextScreenshotButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(nextScreenshotButton, 3);
        captionRow.Children.Add(nextScreenshotButton);

        var gallery = new StackPanel();
        gallery.Children.Add(imageFrame);
        gallery.Children.Add(captionRow);
        UpdateScreenshotGallery(screenshots);
        screenshotGallery = new Border
        {
            Background = Brushes.White,
            Child = gallery
        };
        return screenshotGallery;
    }

    private void MoveScreenshot(int offset)
    {
        if (articleList.SelectedItem is not HelpArticle { Screenshots.Length: > 0 } article)
            return;

        screenshotIndex = Math.Clamp(screenshotIndex + offset, 0, article.Screenshots.Length - 1);
        UpdateScreenshotGallery(article.Screenshots);
    }

    private void UpdateScreenshotGallery(HelpScreenshot[] screenshots)
    {
        screenshotIndex = Math.Clamp(screenshotIndex, 0, screenshots.Length - 1);
        var screenshot = screenshots[screenshotIndex];
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.UriSource = new Uri($"pack://application:,,,/{screenshot.ResourcePath}", UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        screenshotImage.Source = image;
        AutomationProperties.SetName(screenshotImage, screenshot.Caption);
        screenshotCaption.Text = screenshot.Caption;
        screenshotCount.Text = $"{screenshotIndex + 1} / {screenshots.Length}";
        previousScreenshotButton.IsEnabled = screenshotIndex > 0;
        nextScreenshotButton.IsEnabled = screenshotIndex < screenshots.Length - 1;
    }

    private void MoveSelection(int offset)
    {
        if (visibleArticles.Count == 0)
            return;

        var nextIndex = Math.Clamp(articleList.SelectedIndex + offset, 0, visibleArticles.Count - 1);
        articleList.SelectedIndex = nextIndex;
        articleList.ScrollIntoView(articleList.SelectedItem);
    }

    private void UpdateNavigation()
    {
        var index = articleList.SelectedIndex;
        pageCount.Text = visibleArticles.Count == 0 ? "0 Themen" : $"{index + 1} von {visibleArticles.Count} Themen";
        previousButton.IsEnabled = index > 0;
        nextButton.IsEnabled = index >= 0 && index < visibleArticles.Count - 1;
    }

    private static Border BuildIllustration(string type)
    {
        var frame = new Border
        {
            Height = 154,
            Background = Brush("#F4F7FA"),
            BorderBrush = Brush("#D8E0E8"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };
        var diagram = new Grid();
        diagram.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        diagram.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        diagram.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labels = type switch
        {
            "catalogs" => new[] { "Katalog", "Liste", "Einträge" },
            "items" => new[] { "Eintrag", "Werte", "Position" },
            "search" => new[] { "Suchbegriff", "Treffer", "Auswählen" },
            "template" => new[] { "Vorlage", "Feld wählen", "Ziehen" },
            "photos" => new[] { "Importieren", "Foto wählen", "Zuordnen" },
            "print" => new[] { "Aktuelle Liste", "Ganzer Katalog", "PDF / Druck" },
            "backup" => new[] { "Katalog", "Export", "Sicherung" },
            _ => new[] { "Katalog wählen", "Liste öffnen", "Einträge pflegen" }
        };

        for (var index = 0; index < labels.Length; index++)
        {
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brush(index == 1 && type == "template" ? "#1677D2" : "#D8E0E8"),
                BorderThickness = new Thickness(index == 1 && type == "template" ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(index == 0 ? 0 : 7, 16, index == 2 ? 0 : 7, 16),
                Padding = new Thickness(10),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"0{index + 1}",
                            Foreground = Brush("#1677D2"),
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold
                        },
                        new TextBlock
                        {
                            Text = labels[index],
                            Foreground = Brush("#18344F"),
                            FontWeight = FontWeights.SemiBold,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 4, 0, 0)
                        }
                    }
                }
            };
            Grid.SetColumn(card, index);
            diagram.Children.Add(card);
        }
        frame.Child = diagram;
        return frame;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color)!);

    private sealed record HelpArticle(
        string Category,
        string Title,
        string Summary,
        string[] Steps,
        string Tip,
        string Illustration,
        HelpScreenshot[]? Screenshots = null)
    {
        public string SearchText => $"{Category} {Title} {Summary} {string.Join(" ", Steps)} {Tip}";
    }

    private sealed record HelpScreenshot(string ResourcePath, string Caption);
}
