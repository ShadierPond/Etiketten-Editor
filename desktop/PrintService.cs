using System.IO;
using System.IO.Compression;
using System.Printing;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using QRCoder;
using ZXing;
using ZXing.Common;

namespace FuniturenEditor.Desktop;

public static class PrintService
{
    private const double Dpi = 96;
    private const double MillimetresPerInch = 25.4;

    public static void PrintFurniture(Window owner, CatalogData catalog, IEnumerable<CatalogPage> pages, PhotoLibrary photoLibrary)
    {
        ShowPrintDialog(owner, CreateFurnitureDocument(catalog, pages, photoLibrary), "Etiketten",
            catalog.FurniturePrintLayout.PaperFormat);
    }

    public static void SaveFurniturePdf(
        Window owner,
        CatalogData catalog,
        IEnumerable<CatalogPage> pages,
        PhotoLibrary photoLibrary,
        string fileName) =>
        SaveDocumentAsPdf(owner, CreateFurnitureDocument(catalog, pages, photoLibrary), fileName);

    public static FixedDocument CreateFurnitureDocument(
        CatalogData catalog,
        IEnumerable<CatalogPage> pages,
        PhotoLibrary photoLibrary,
        CatalogItem? highlightedItem = null,
        Action<CatalogItem>? onPreviewItemClicked = null,
        ICollection<Border>? previewCells = null)
    {
        var layout = catalog.FurniturePrintLayout;
        layout.Normalize();
        var orientation = layout.Orientation == "Portrait"
            ? PageOrientation.Portrait
            : PageOrientation.Landscape;
        var document = CreateDocument(orientation, layout.PaperFormat);
        foreach (var catalogPage in pages)
        {
            var (paperWidthCm, paperHeightCm) = FurniturePrintLayout.GetPaperDimensionsCm(layout.PaperFormat);
            var pageWidth = Cm(orientation == PageOrientation.Portrait ? paperWidthCm : paperHeightCm);
            var pageHeight = Cm(orientation == PageOrientation.Portrait ? paperHeightCm : paperWidthCm);
            var labelWidth = Cm(layout.CellWidthCm);
            var labelHeight = Cm(layout.CellHeightCm);
            var horizontalGap = Cm(layout.HorizontalGapCm);
            var verticalGap = Cm(layout.VerticalGapCm);
            var maxColumnsOnPage = Math.Max(1,
                (int)((pageWidth - Mm(16) + horizontalGap) / (labelWidth + horizontalGap)));
            var columns = Math.Clamp(layout.Columns, 1, Math.Min(layout.TileCount, maxColumnsOnPage));
            var rowsPerPage = Math.Max(1, (int)((pageHeight - Mm(28)) / (labelHeight + verticalGap)));
            var capacityPerPage = columns * rowsPerPage;
            var pageCount = Math.Max(1, (int)Math.Ceiling(layout.TileCount / (double)capacityPerPage));
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var page = CreatePage(pageWidth, pageHeight);
                AddText(page, "Etiketten Editor", Mm(8), Mm(7), 160, 20, 9, Brushes.SlateGray);
                AddText(page, catalogPage.Name, pageWidth / 2 - 170, Mm(7), 340, 20, 12, Brushes.Black, FontWeights.SemiBold);
                AddText(page, $"{layout.PaperFormat} · {layout.TileCount} Etiketten · {layout.Columns} Spalten" +
                    (pageCount > 1 ? $" · Seite {pageIndex + 1}/{pageCount}" : ""),
                    pageWidth - Mm(62), Mm(7), Mm(54), 20, 9, Brushes.SlateGray);
                var startIndex = pageIndex * capacityPerPage;
                var itemsOnPage = Math.Min(capacityPerPage, layout.TileCount - startIndex);
                var rows = (int)Math.Ceiling(itemsOnPage / (double)columns);
                var totalWidth = labelWidth * columns + horizontalGap * (columns - 1);
                var totalHeight = labelHeight * rows + verticalGap * (rows - 1);
                var left = (page.Width - totalWidth) / 2;
                var top = (page.Height - totalHeight) / 2 + Mm(8);
                for (var localIndex = 0; localIndex < itemsOnPage; localIndex++)
                {
                    var index = startIndex + localIndex;
                    var item = index < catalogPage.Items.Count ? catalogPage.Items[index] : new CatalogItem();
#if INTERNAL_DIAGNOSTICS
                using var cellDiagnostic = AppDiagnostics.Measure(
                    "preview.cell.build",
                    () => $"position={index + 1};barcodeField={IsBarcodeField(layout)}");
#endif
                    var cell = CreateFurnitureCell(
                        catalog, item, photoLibrary, layout,
                        ReferenceEquals(item, highlightedItem), onPreviewItemClicked);
                    previewCells?.Add(cell);
                    var column = localIndex % columns;
                    var row = localIndex / columns;
                    FixedPage.SetLeft(cell, left + column * (labelWidth + horizontalGap));
                    FixedPage.SetTop(cell, top + row * (labelHeight + verticalGap));
                    page.Children.Add(cell);
                }
                AddPage(document, page);
            }
        }
        return document;
    }

    public static Border CreateFurnitureCellPreview(CatalogData catalog, CatalogItem item, PhotoLibrary photoLibrary, FurniturePrintLayout layout) =>
        CreateFurnitureCell(catalog, item, photoLibrary, layout);

    public static void PrintEmployees(Window owner, IEnumerable<EmployeePage> branches, EmployeeLayout layout)
    {
        ShowPrintDialog(owner, CreateEmployeeDocument(branches, layout), "Mitarbeitendencodes");
    }

    public static void SaveEmployeesPdf(
        Window owner,
        IEnumerable<EmployeePage> branches,
        EmployeeLayout layout,
        string fileName) =>
        SaveDocumentAsPdf(owner, CreateEmployeeDocument(branches, layout), fileName);

    public static FixedDocument CreateEmployeeDocument(IEnumerable<EmployeePage> branches, EmployeeLayout layout)
    {
        var document = CreateDocument(PageOrientation.Portrait);
        foreach (var branch in branches)
        {
            var cardWidth = Mm(Math.Clamp(layout.Width, 35, 180));
            var cardHeight = Mm(Math.Clamp(layout.Height, 12, 80));
            var leftMargin = Mm(8);
            var topMargin = Mm(12);
            var columns = Math.Max(1, (int)((Mm(210) - leftMargin * 2) / cardWidth));
            var rowsPerPage = Math.Max(1, (int)((Mm(297) - topMargin * 2) / cardHeight));
            var pageCount = Math.Max(1, (int)Math.Ceiling(branch.Employees.Count / (double)(columns * rowsPerPage)));
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var page = CreatePage(Mm(210), Mm(297));
                AddText(page, $"Furnituren Editor · Mitarbeitendencodes · {branch.Name}", Mm(8), Mm(8), 700, 22, 10, Brushes.Black, FontWeights.SemiBold);
                var start = pageIndex * columns * rowsPerPage;
                var end = Math.Min(branch.Employees.Count, start + columns * rowsPerPage);
                for (var itemIndex = start; itemIndex < end; itemIndex++)
                {
                    var employee = branch.Employees[itemIndex];
                    var position = itemIndex - start;
                    var card = CreateEmployeeCard(employee, layout, cardWidth, cardHeight);
                    FixedPage.SetLeft(card, leftMargin + position % columns * cardWidth);
                    FixedPage.SetTop(card, topMargin + Mm(7) + position / columns * cardHeight);
                    page.Children.Add(card);
                }
                AddPage(document, page);
            }
        }
        return document;
    }

    public static void ExportQrBarcodeZip(Window owner, CatalogData catalog)
    {
        var layout = catalog.FurniturePrintLayout;
        layout.Normalize();
        var products = catalog.Pages.SelectMany(page => page.Items)
            .Where(item => !string.IsNullOrWhiteSpace(GetQrContent(item, layout)))
            .ToList();
        if (products.Count == 0)
        {
            MessageBox.Show(owner, "Es wurden keine Artikel mit einem QR-/Barcodewert gefunden.",
                "QR-/Barcode-Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "QR-/Barcode-Codes speichern",
            Filter = "ZIP-Datei (*.zip)|*.zip",
            FileName = "etiketten-codes.zip",
            AddExtension = true
        };
        if (dialog.ShowDialog(owner) != true)
            return;

        try
        {
            using var archive = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create);
            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in products)
            {
                var qrContent = GetQrContent(item, layout);
                var baseName = SafeFileName(qrContent);
                nameCounts.TryGetValue(baseName, out var count);
                nameCounts[baseName] = ++count;
                var entry = archive.CreateEntry($"{baseName}{(count > 1 ? $" ({count})" : "")}.png");
                using var stream = entry.Open();
                stream.Write(CreateQrBarcodeBytes(item, layout));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Die QR-/Barcode-Codes konnten nicht erstellt werden.\n\n{ex.Message}",
                "QR-/Barcode-Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static FixedDocument CreateDocument(PageOrientation orientation, string paperFormat = "A4")
    {
        var (paperWidthCm, paperHeightCm) = FurniturePrintLayout.GetPaperDimensionsCm(paperFormat);
        var isLandscape = orientation == PageOrientation.Landscape;
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = isLandscape
            ? new Size(Cm(paperHeightCm), Cm(paperWidthCm))
            : new Size(Cm(paperWidthCm), Cm(paperHeightCm));
        return document;
    }

    private static FixedPage CreatePage(double width, double height) => new()
    {
        Width = width,
        Height = height,
        Background = Brushes.White
    };

    private static void AddPage(FixedDocument document, FixedPage page)
    {
        var pageContent = new PageContent { Child = page };
        document.Pages.Add(pageContent);
    }

    private static void ShowPrintDialog(Window owner, FixedDocument document, string title, string paperFormat = "A4")
    {
        var preview = new PrintPreviewWindow(document, title) { Owner = owner };
        if (preview.ShowDialog() != true)
            return;

        var dialog = new PrintDialog();
        dialog.PrintTicket ??= new PrintTicket();
        ConfigurePageSize(dialog, document, paperFormat);
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            dialog.PrintDocument(document.DocumentPaginator, title);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Der Druckauftrag konnte nicht gestartet werden.\n\n{ex.Message}",
                "Drucken", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void SaveDocumentAsPdf(Window owner, FixedDocument document, string fileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Als PDF speichern",
            Filter = "PDF-Datei (*.pdf)|*.pdf",
            FileName = $"{SafeFileName(fileName)}.pdf",
            AddExtension = true
        };
        if (dialog.ShowDialog(owner) != true)
            return;

        try
        {
            var paginator = document.DocumentPaginator;
            paginator.ComputePageCount();
            using var pdf = new PdfDocument();
            for (var pageIndex = 0; pageIndex < paginator.PageCount; pageIndex++)
            {
                var documentPage = paginator.GetPage(pageIndex);
                var bitmap = RenderPage(documentPage);
                var imageData = EncodePng(bitmap);
                using var imageStream = new MemoryStream(imageData, writable: false);
                using var image = XImage.FromStream(imageStream);

                var pdfPage = pdf.AddPage();
                pdfPage.Width = XUnit.FromPoint(documentPage.Size.Width / Dpi * 72);
                pdfPage.Height = XUnit.FromPoint(documentPage.Size.Height / Dpi * 72);
                using var graphics = XGraphics.FromPdfPage(pdfPage);
                graphics.DrawImage(image, 0, 0, pdfPage.Width.Point, pdfPage.Height.Point);
            }

            pdf.Save(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Die PDF-Datei konnte nicht erstellt werden.\n\n{ex.Message}",
                "PDF speichern", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static RenderTargetBitmap RenderPage(DocumentPage documentPage)
    {
        const double renderDpi = 192;
        var scale = renderDpi / Dpi;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(documentPage.Size.Width * scale),
            (int)Math.Ceiling(documentPage.Size.Height * scale),
            renderDpi,
            renderDpi,
            PixelFormats.Pbgra32);
        bitmap.Render(documentPage.Visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void ConfigurePageSize(PrintDialog dialog, FixedDocument document, string paperFormat)
    {
        var pageSizeName = paperFormat switch
        {
            "A3" => System.Printing.PageMediaSizeName.ISOA3,
            "A5" => System.Printing.PageMediaSizeName.ISOA5,
            "A6" => System.Printing.PageMediaSizeName.ISOA6,
            "Letter" => System.Printing.PageMediaSizeName.NorthAmericaLetter,
            "Legal" => System.Printing.PageMediaSizeName.NorthAmericaLegal,
            _ => System.Printing.PageMediaSizeName.ISOA4
        };
        dialog.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(pageSizeName);
        dialog.PrintTicket.PageOrientation = document.DocumentPaginator.PageSize.Width > document.DocumentPaginator.PageSize.Height
            ? PageOrientation.Landscape
            : PageOrientation.Portrait;
    }

    private static Border CreateEmployeeCard(Employee employee, EmployeeLayout layout, double width, double height)
    {
        var qrValue = string.IsNullOrWhiteSpace(employee.Code) ? employee.Number : employee.Code;
        var qr = qrValue.Length == 0 ? null : CreateQrImage(qrValue, Math.Max(34, Math.Min(height - 12, Mm(15))));
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        text.Children.Add(new TextBlock
        {
            Text = employee.Name,
            FontSize = Math.Max(6, layout.NameSize * 96 / 72),
            FontWeight = layout.NameBold ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = width - 55
        });
        text.Children.Add(new TextBlock
        {
            Text = employee.Number,
            FontSize = Math.Max(5, layout.NumberSize * 96 / 72),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

        var panel = new DockPanel { LastChildFill = true, Margin = new Thickness(3) };
        if (qr is not null)
        {
            if (layout.QrPosition is "left" or "right")
            {
                DockPanel.SetDock(qr, layout.QrPosition == "left" ? Dock.Left : Dock.Right);
                panel.Children.Add(qr);
            }
            else
            {
                DockPanel.SetDock(qr, layout.QrPosition == "above" ? Dock.Top : Dock.Bottom);
                panel.Children.Add(qr);
            }
        }
        panel.Children.Add(text);
        return new Border
        {
            Width = width,
            Height = height,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.5),
            Child = panel
        };
    }

    private static Border CreateFurnitureCell(
        CatalogData catalog,
        CatalogItem item,
        PhotoLibrary photoLibrary,
        FurniturePrintLayout layout,
        bool highlighted = false,
        Action<CatalogItem>? onPreviewItemClicked = null)
    {
        var width = Cm(layout.CellWidthCm);
        var height = Cm(layout.CellHeightCm);
        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent
        };
        var codeContent = GetQrContent(item, layout);
        foreach (var field in layout.Elements)
        {
            object? content = field.Kind switch
            {
                "Photo" => GetPhotoContent(catalog, item, field, photoLibrary),
                "Qr" => CreateCodeContent(GetFieldCodeContent(item, field, layout, codeContent), barcode: false),
                "Barcode" => CreateCodeContent(GetFieldCodeContent(item, field, layout, codeContent), barcode: true),
                _ => GetFieldText(item, field, layout)
            };
            AddFurnitureElement(canvas, field, content, width, height);
        }
        var border = new Border
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.55),
            ClipToBounds = true,
            Child = canvas
        };
        border.Tag = item;
        if (highlighted)
            SetFurnitureCellHighlighted(border, true);
        if (onPreviewItemClicked is not null)
        {
            border.Cursor = Cursors.Hand;
            border.PreviewMouseLeftButtonDown += (_, args) =>
            {
                onPreviewItemClicked(item);
                args.Handled = true;
            };
        }
        return border;
    }

    public static Border? UpdateFurniturePreviewHighlight(
        IEnumerable<Border> previewCells,
        CatalogItem? highlightedItem,
        Border? previousCell)
    {
        var highlightedCell = highlightedItem is null
            ? null
            : previewCells.FirstOrDefault(cell => ReferenceEquals(cell.Tag, highlightedItem));
        if (ReferenceEquals(highlightedCell, previousCell))
            return previousCell;

        if (previousCell is not null)
            SetFurnitureCellHighlighted(previousCell, false);
        if (highlightedCell is not null)
            SetFurnitureCellHighlighted(highlightedCell, true);
        return highlightedCell;
    }

    private static void SetFurnitureCellHighlighted(Border border, bool highlighted)
    {
        border.BorderBrush = highlighted ? Brushes.DarkOrange : Brushes.Black;
        border.BorderThickness = new Thickness(highlighted ? 2 : 0.55);
    }

    private static void AddText(FixedPage page, string value, double left, double top, double width, double height,
        double fontSize, Brush foreground, FontWeight? weight = null)
    {
        var text = new TextBlock
        {
            Text = value,
            Width = width,
            Height = height,
            FontSize = fontSize,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = foreground,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        FixedPage.SetLeft(text, left);
        FixedPage.SetTop(text, top);
        page.Children.Add(text);
    }

    private static Image CreateQrImage(string content) => new()
    {
        Source = CreateQrBitmap(content),
        Stretch = Stretch.Uniform,
    };

    private static Image CreateQrImage(string content, double size) => new()
    {
        Source = CreateQrBitmap(content),
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Image CreatePhotoImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 256;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform
        };
    }

    private static void AddFurnitureElement(
        Canvas canvas,
        PrintElementLayout layout,
        object? content,
        double cellWidth,
        double cellHeight)
    {
        var width = Cm(layout.Width);
        var height = Cm(layout.Height);
        if (content is null || width <= 0 || height <= 0)
            return;

        FrameworkElement element;
        if (content is FrameworkElement visual)
        {
            element = visual;
        }
        else
        {
            var text = (string)content;
            if (string.IsNullOrWhiteSpace(text))
                return;
            element = new TextBlock
            {
                Text = text,
                FontSize = layout.AutoShrinkText != false
                    ? FitTextFontSize(text, layout.FontSize * 96 / 72, width, height,
                        layout.Bold ? FontWeights.SemiBold : FontWeights.Normal)
                    : layout.FontSize * 96 / 72,
                FontWeight = layout.Bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextAlignment = TextAlignment.Center,
                TextTrimming = layout.AutoShrinkText != false ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        element.Width = width;
        element.Height = height;
        element.ClipToBounds = true;
        element.Tag = layout;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new RotateTransform(layout.RotationDegrees);
        Canvas.SetLeft(element, Math.Min(Cm(layout.X), Math.Max(0, cellWidth - width)));
        Canvas.SetTop(element, Math.Min(Cm(layout.Y), Math.Max(0, cellHeight - height)));
        canvas.Children.Add(element);
    }

    private static double FitTextFontSize(
        string text,
        double requestedSize,
        double width,
        double height,
        FontWeight weight)
    {
        var typeface = new Typeface(
            new FontFamily("Segoe UI"),
            FontStyles.Normal,
            weight,
            FontStretches.Normal);
        for (var size = requestedSize; size >= 4; size -= 0.25)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                Brushes.Black,
                1)
            {
                MaxTextWidth = width
            };
            if (formatted.Height <= height && formatted.Width <= width)
                return size;
        }
        return 4;
    }

    private static BitmapImage CreateQrBitmap(string content)
    {
        using var stream = new MemoryStream(CreateQrBytes(content));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] CreateQrBytes(string content)
    {
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(qrData).GetGraphic(8);
    }

    private static byte[] RenderBarcodeBytes(string content)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Width = 960, Height = 240, Margin = 10, PureBarcode = true }
        };
        var pixels = writer.Write(content);
        var bitmap = BitmapSource.Create(
            pixels.Width,
            pixels.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels.Pixels,
            pixels.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static Image CreateBarcodeImage(string content)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Width = 480,
                Height = 120,
                Margin = 10,
                PureBarcode = true
            }
        };
        var pixels = writer.Write(content);
        var bitmap = BitmapSource.Create(
            pixels.Width,
            pixels.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels.Pixels,
            pixels.Width * 4);
        bitmap.Freeze();
        return new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform
        };
    }

    public static byte[] CreateQrBarcodeBytes(CatalogItem item, FurniturePrintLayout layout)
    {
        var content = GetQrContent(item, layout);
        if (string.IsNullOrWhiteSpace(content))
            return [];
        return IsBarcodeField(layout) ? RenderBarcodeBytes(content) : CreateQrBytes(content);
    }

    public static bool IsBarcodeField(FurniturePrintLayout layout) =>
        layout.Elements.Any(field => field.Kind == "Barcode");

    public static string GetQrContent(CatalogItem item, FurniturePrintLayout layout)
    {
        if (!layout.QrCode.Enabled)
            return "";
        var value = GetSourceValue(item, layout, layout.QrCode.SourceField);
        if (value.Length == 0 && layout.QrCode.UseFallback)
            value = GetSourceValue(item, layout, layout.QrCode.FallbackField);
        return value.Length == 0 ? "" : layout.QrCode.Prefix + value + layout.QrCode.Suffix;
    }

    private static object? CreateCodeContent(string content, bool barcode)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        return barcode ? CreateBarcodeImage(content) : CreateQrImage(content);
    }

    private static string GetFieldCodeContent(
        CatalogItem item,
        PrintElementLayout field,
        FurniturePrintLayout layout,
        string configuredContent)
    {
        var value = item.FieldValues.GetValueOrDefault(field.Id, "").Trim();
        if (value.Length == 0)
            return configuredContent;
        return layout.QrCode.Prefix + value + layout.QrCode.Suffix;
    }

    private static Image? GetPhotoContent(
        CatalogData catalog,
        CatalogItem item,
        PrintElementLayout field,
        PhotoLibrary photoLibrary)
    {
        var key = item.FieldValues.GetValueOrDefault(field.Id, "");
        if (string.IsNullOrWhiteSpace(key))
            key = item.Photo;
        var path = photoLibrary.GetPhotoPath(catalog, key);
        return path is null ? null : CreatePhotoImage(path);
    }

    private static string GetFieldText(CatalogItem item, PrintElementLayout field, FurniturePrintLayout layout)
    {
        var value = item.FieldValues.GetValueOrDefault(field.Id, "");
        if (string.IsNullOrWhiteSpace(value))
            value = GetLegacySourceValue(item, field.Source);
        if (field.Source == "Beco")
            value = DisplayBecoText(item, layout.MissingBecoText);
        return field.Kind == "Number" && value.Length > 0
            ? value.Trim() + field.Suffix
            : value.Trim();
    }

    private static string GetSourceValue(CatalogItem item, FurniturePrintLayout layout, string fieldId)
    {
        if (item.FieldValues.TryGetValue(fieldId, out var value))
            return value.Trim();
        var field = layout.Fields.FirstOrDefault(candidate => candidate.Id == fieldId);
        return field is null ? "" : GetLegacySourceValue(item, field.Source);
    }

    private static string GetLegacySourceValue(CatalogItem item, string source) =>
        source switch
        {
            "Title" => item.Title.Trim(),
            "Beco" => item.Beco.Trim(),
            "Ref" => item.Ref.Trim(),
            "Price" => item.Price.Trim(),
            "SalesPrice" => item.SalesPrice.Trim(),
            "Ek" => item.Ek.Trim(),
            _ => ""
        };

    private static string BecoText(string value)
    {
        var beco = value.Trim();
        if (beco.Length == 0)
            return "";
        return beco.StartsWith("BECO", StringComparison.OrdinalIgnoreCase)
            ? "BECO" + beco[4..].Trim()
            : "BECO" + beco;
    }

    private static string DisplayBecoText(CatalogItem item, string missingBecoText)
    {
        var beco = BecoText(item.Beco);
        if (beco.Length > 0)
            return beco;

        return HasFurnitureContent(item) ? missingBecoText : "";
    }

    private static bool HasFurnitureContent(CatalogItem item) =>
        new[]
        {
            item.Title, item.Ref, item.Photo, item.Price, item.Ek, item.SalesPrice
        }.Any(value => !string.IsNullOrWhiteSpace(value));

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? ' ' : character).ToArray());
        sanitized = string.Join(' ', sanitized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (sanitized.Length > 120)
            sanitized = sanitized[..120].Trim();
        if (sanitized.Length == 0 ||
            new[] { "CON", "PRN", "AUX", "NUL" }.Contains(sanitized, StringComparer.OrdinalIgnoreCase) ||
            (sanitized.Length == 4 &&
             (sanitized.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
              sanitized.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             sanitized[3] is >= '1' and <= '9'))
            return "Code";
        return sanitized;
    }

    private static double Mm(double value) => value * Dpi / MillimetresPerInch;
    private static double Cm(double value) => Mm(value * 10);
}
