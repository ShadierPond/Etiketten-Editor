using System.IO;
using System.Xml;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Dom;
using SharpVectors.Renderers.Wpf;

namespace LabelEditor.Desktop;

internal static class SvgImageConverter
{
    private const int MinimumRasterDimension = 512;
    private const int MaximumRasterDimension = 2048;
    private const int MaximumSvgBytes = 20 * 1024 * 1024;
    private const double MaximumSvgDimension = 1_000_000;

    public static DrawingImage LoadDrawingImage(Stream source)
    {
        var drawing = ReadDrawing(source);
        drawing.Freeze();
        return new DrawingImage(drawing);
    }

    public static byte[] ConvertToPng(byte[] svgBytes)
    {
        if (svgBytes.Length == 0 || svgBytes.Length > MaximumSvgBytes)
            throw new InvalidDataException("Die SVG-Datei ist leer oder überschreitet die zulässige Größe.");

        using var source = new MemoryStream(svgBytes, writable: false);
        var drawing = ReadDrawing(source);
        var bounds = drawing.Bounds;
        if (bounds.IsEmpty || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
            bounds.Width <= 0 || bounds.Height <= 0 ||
            bounds.Width > MaximumSvgDimension || bounds.Height > MaximumSvgDimension)
            throw new InvalidDataException("Die SVG-Datei enthält keine gültige Bildfläche.");

        var longestSide = Math.Max(bounds.Width, bounds.Height);
        var rasterLongestSide = (int)Math.Clamp(
            Math.Ceiling(longestSide),
            MinimumRasterDimension,
            MaximumRasterDimension);
        var scale = rasterLongestSide / longestSide;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new MatrixTransform(new Matrix(
                scale, 0, 0, scale, -bounds.X * scale, -bounds.Y * scale)));
            context.DrawDrawing(drawing);
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var result = new MemoryStream();
        encoder.Save(result);
        return result.ToArray();
    }

    private static DrawingGroup ReadDrawing(Stream source)
    {
        if (source.CanSeek && source.Length - source.Position > MaximumSvgBytes)
            throw new InvalidDataException("Die SVG-Datei überschreitet die zulässige Größe.");

        var settings = new WpfDrawingSettings
        {
            ExternalResourcesAccessMode = ExternalResourcesAccessModes.Ignore
        };
        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumSvgBytes
        };
        using var xmlReader = XmlReader.Create(source, xmlSettings);
        using var reader = new FileSvgReader(settings);
        try
        {
            return reader.Read(xmlReader) ??
                throw new InvalidDataException("Die SVG-Datei konnte nicht gelesen werden.");
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("Die SVG-Datei ist ungültig oder enthält nicht unterstützte XML-Inhalte.", ex);
        }
    }
}
