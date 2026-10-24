using System.Text;
using FuniturenEditor.Desktop;
using QRCoder;

namespace FuniturenEditor_WinUI;

/// <summary>
/// Creates A4 layouts with explicit physical dimensions for the in-app preview
/// and Windows print dialog.
/// </summary>
internal static class A4HtmlExporter
{
    public static string CreateFurnitureHtml(CatalogData catalog, CatalogPage page, StoragePhotoLibrary photos)
        => CreateFurnitureHtml(catalog, [page], photos);

    public static string CreateFurnitureHtml(
        CatalogData catalog,
        IEnumerable<CatalogPage> pages,
        StoragePhotoLibrary photos)
    {
        var layout = catalog.FurniturePrintLayout;
        layout.Normalize();
        var catalogPages = pages.ToList();
        var html = new StringBuilder(StartHtml("Furnituren-Etiketten", "landscape"));
        var width = layout.CellWidthCm;
        var height = layout.CellHeightCm;
        var left = (29.7 - width * 5) / 2;
        var top = (21 - height * 3) / 2 + 0.8;
        for (var pageIndex = 0; pageIndex < catalogPages.Count; pageIndex++)
        {
            var page = catalogPages[pageIndex];
            html.Append("<section class=\"catalog-sheet\"");
            if (pageIndex < catalogPages.Count - 1)
                html.Append(" style=\"break-after:page;page-break-after:always\"");
            html.Append("><header><strong>Furnituren Editor</strong><span>")
                .Append(Encode(page.Name))
                .Append("</span><small>A4 · 15 Etiketten · 5 × 3</small></header><main class=\"labels\">");
            for (var index = 0; index < 15; index++)
            {
                var item = page.Items[index];
                var x = left + index % 5 * width;
                var y = top + index / 5 * height;
                html.Append("<article class=\"label\" style=\"left:")
                    .Append(Cm(x)).Append("cm;top:").Append(Cm(y)).Append("cm;width:")
                    .Append(Cm(width)).Append("cm;height:").Append(Cm(height)).Append("cm\">");
                AppendPhoto(html, layout.Photo, photos.GetPhotoPath(catalog, item.Photo));
                var qrContent = item.QuickService
                    ? string.Join(' ', new[] { item.Title.Trim(), BecoText(item.Beco) }.Where(text => text.Length > 0))
                    : item.Ref.Trim();
                if (!string.IsNullOrWhiteSpace(qrContent))
                    AppendImage(html, layout.Qr, "data:image/png;base64," + Convert.ToBase64String(CreateQrBytes(qrContent)), "QR code");
                AppendText(html, layout.Title, item.Title);
                AppendText(html, layout.Beco, string.IsNullOrWhiteSpace(item.Beco) ? "" : BecoText(item.Beco));
                AppendText(html, layout.Ref, item.Ref);
                AppendText(html, layout.Price, item.Price);
                AppendText(html, layout.Ek, item.Ek);
                html.Append("</article>");
            }
            html.Append("</main></section>");
        }
        return html.Append("</body></html>").ToString();
    }

    public static string CreateEmployeesHtml(EmployeePage branch, EmployeeLayout layout)
        => CreateEmployeesHtml([branch], layout);

    public static string CreateEmployeesHtml(IEnumerable<EmployeePage> branches, EmployeeLayout layout)
    {
        var width = Math.Clamp(layout.Width, 35, 180);
        var height = Math.Clamp(layout.Height, 12, 80);
        var html = new StringBuilder(StartHtml(
            "Mitarbeiterkarten",
            "portrait",
            layout.NameSize,
            layout.NumberSize,
            Math.Min(height * 0.75, 16),
            layout.NameBold));
        var employeePages = branches.ToList();
        for (var branchIndex = 0; branchIndex < employeePages.Count; branchIndex++)
        {
            var branch = employeePages[branchIndex];
            html.Append("<section");
            if (branchIndex > 0)
                html.Append(" style=\"break-before:page;page-break-before:always\"");
            html.Append("><header><strong>Furnituren Editor</strong><span>")
                .Append(Encode(branch.Name))
                .Append("</span><small>Mitarbeiterkarten</small></header><main class=\"employee-grid\">");
            foreach (var employee in branch.Employees)
            {
                html.Append("<article class=\"employee-card\" style=\"width:")
                    .Append(width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("mm;height:")
                    .Append(height.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("mm\">");
                if (string.Equals(layout.QrPosition, "left", StringComparison.OrdinalIgnoreCase))
                    AppendEmployeeQr(html, employee.Code);
                html.Append("<div class=\"employee-copy\"><strong>")
                    .Append(Encode(employee.Name))
                    .Append("</strong><span>")
                    .Append(Encode(employee.Number))
                    .Append("</span></div>");
                if (!string.Equals(layout.QrPosition, "left", StringComparison.OrdinalIgnoreCase))
                    AppendEmployeeQr(html, employee.Code);
                html.Append("</article>");
            }
            html.Append("</main></section>");
        }
        return html.Append("</body></html>").ToString();
    }

    public static byte[] CreateQrBytes(string content)
    {
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(qrData).GetGraphic(8);
    }

    private static string StartHtml(
        string title,
        string orientation,
        double employeeNameSize = 8,
        double employeeNumberSize = 5.5,
        double employeeQrSize = 12,
        bool employeeNameBold = true) => $$"""
        <!doctype html>
        <html lang="de"><head><meta charset="utf-8"><title>{{Encode(title)}}</title>
        <style>
        @page { size:A4 {{orientation}}; margin:0; }
        * { box-sizing:border-box; }
        html,body { margin:0; padding:0; color:#111; font-family:Arial, sans-serif; }
        .catalog-sheet { width:297mm; height:210mm; overflow:hidden; }
        header { height:12mm; padding:5mm 8mm 0; display:flex; justify-content:space-between; align-items:flex-start; font-size:9pt; color:#555; }
        header span { color:#111; font-weight:600; font-size:11pt; }
        header small { font-size:8pt; }
        .labels { position:relative; width:297mm; height:198mm; }
        .label { position:absolute; border:.2mm solid #888; overflow:hidden; }
        .field { position:absolute; overflow:hidden; line-height:1.1; white-space:pre-wrap; overflow-wrap:anywhere; }
        .employee-grid { padding:12mm 8mm; display:flex; flex-wrap:wrap; align-content:flex-start; gap:3mm; }
        .employee-card { border:.25mm solid #555; padding:1.5mm; display:flex; align-items:center; gap:2mm; page-break-inside:avoid; }
        .employee-copy { display:flex; flex:1; min-width:0; flex-direction:column; justify-content:center; overflow:hidden; }
        .employee-copy strong { font-size:{{layoutFont(employeeNameSize)}}; font-weight:{{(employeeNameBold ? "700" : "400")}}; overflow:hidden; }
        .employee-copy span { font-size:{{layoutFont(employeeNumberSize)}}; }
        .employee-qr { width:{{employeeQrSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}}mm; height:{{employeeQrSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}}mm; object-fit:contain; }
        @media print { .employee-card { break-inside:avoid; } }
        </style></head><body>
        """;

    private static void AppendPhoto(StringBuilder html, PrintElementLayout element, string? path)
    {
        if (path is null || !File.Exists(path))
            return;
        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
        AppendImage(html, element, $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}", "Artikelbild");
    }

    private static void AppendImage(StringBuilder html, PrintElementLayout element, string source, string alt)
    {
        html.Append("<img class=\"field\" alt=\"").Append(Encode(alt)).Append("\" src=\"")
            .Append(source).Append("\" style=\"")
            .Append(PositionStyle(element)).Append("object-fit:contain\">");
    }

    private static void AppendText(StringBuilder html, PrintElementLayout element, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        html.Append("<div class=\"field\" style=\"")
            .Append(PositionStyle(element))
            .Append("font-size:")
            .Append(element.FontSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
            .Append("pt;font-weight:")
            .Append(element.Bold ? "700" : "400")
            .Append("\">")
            .Append(Encode(value))
            .Append("</div>");
    }

    private static string PositionStyle(PrintElementLayout element) =>
        $"left:{Cm(element.X)}cm;top:{Cm(element.Y)}cm;width:{Cm(element.Width)}cm;height:{Cm(element.Height)}cm;";

    private static void AppendEmployeeQr(StringBuilder html, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return;
        html.Append("<img class=\"employee-qr\" alt=\"QR-Code\" src=\"data:image/png;base64,")
            .Append(Convert.ToBase64String(CreateQrBytes(code)))
            .Append("\">");
    }

    private static string Cm(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
    private static string layoutFont(double points) => points.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "pt";

    private static string BecoText(string value)
    {
        var beco = value.Trim();
        if (beco.Length == 0)
            return "";
        return beco.StartsWith("BECO", StringComparison.OrdinalIgnoreCase)
            ? "BECO" + beco[4..].Trim()
            : "BECO" + beco;
    }
}
